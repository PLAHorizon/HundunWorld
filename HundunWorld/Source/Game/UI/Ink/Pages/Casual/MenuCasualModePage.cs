using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Casual
{
    public class MenuCasualModePage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private Float2 _screenSize;

        private InkButton _backButton;
        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkPanel _contentArea;

        private InkPanel _dailySummaryPanel;
        private InkBar _vitalityBar;

        // 顶栏身份/货币/时辰：此前都是构造里的局部变量、没人给它们定位，全部叠在 (0,0)（2026-10-29 截图实测）
        private Label _charNameLabel;
        private Label _charLevelLabel;
        private Label _charSchoolLabel;
        private Label _vitalityValueLabel;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;
        private Label _timePeriodLabel;
        private Label _timeQuarterLabel;
        private Control _avatarBox;
        private InkBar _activityBar;

        private InkButton[] _tabButtons;
        private Label _sampleNote;
        private InkButton _filterButton;

        private ActivityCardView[] _activityCards;

        private InkPanel _rewardPreviewBar;

        // 活跃度汇总 / 奖励总览两块面板的子控件引用：构造时全是局部变量且没有定位，全部叠在 (0,0)。
        private Label _summaryLabel;
        private Label _summaryValue;
        private readonly List<Label> _milestoneLabels = new List<Label>();
        private readonly List<Label> _statNumLabels = new List<Label>();
        private readonly List<Label> _statTextLabels = new List<Label>();
        private Control _summaryIcon;
        private Label _previewTitle;
        private Label _previewSubtitle;
        private Label _previewItemCount;
        private InkButton _previewDetailButton;
        private readonly List<(Label Name, Label Qty)> _previewChips = new List<(Label, Label)>();

        // ------------------------------------------------------------------
        // 活动卡示例数据
        // 直接逐项取自设计稿 menu-casual-mode.html 的 6 张 .activity-card（标题 / 描述 /
        // 进度标签与数值 / 进度条百分比 / 状态标签 / 插画字 / 剩余时间 / 两项奖励）。
        // 接入真实活动数据源时整体替换本数组即可，字段与设计稿元素一一对应。
        // ------------------------------------------------------------------

        /// <summary>活动奖励项规格（设计稿 .reward-item：图标 + 数量 + 名称）。</summary>
        private sealed class ActivityRewardSpec
        {
            /// <summary>数量文案（设计稿 .reward-qty，如 "×500"）</summary>
            public string Qty;

            /// <summary>奖励名称（设计稿 .reward-name，如 "铜钱"）</summary>
            public string Name;

            /// <summary>品质（决定图标描边色，设计稿 ink-q-*）</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>图标字形（设计稿 lucide 图标的引擎侧近似）</summary>
            public string Glyph;

            /// <summary>构造一条奖励规格。</summary>
            public ActivityRewardSpec(string qty, string name, InkWashTheme.InkQuality quality, string glyph)
            {
                Qty = qty;
                Name = name;
                Quality = quality;
                Glyph = glyph;
            }
        }

        /// <summary>活动卡规格（设计稿 .activity-card）。</summary>
        private sealed class ActivityCardSpec
        {
            /// <summary>活动名（设计稿 .card-title）</summary>
            public string Name;

            /// <summary>活动描述（设计稿 .card-desc）</summary>
            public string Desc;

            /// <summary>进度标签（设计稿 .progress-label，如 "连签进度"）</summary>
            public string ProgressLabel;

            /// <summary>进度数值（设计稿 .progress-value，如 "6 / 7 天"）</summary>
            public string ProgressValue;

            /// <summary>进度条百分比（设计稿 .ink-bar-fill width，0-1）</summary>
            public float Progress;

            /// <summary>状态文案（设计稿头部 ink-tag，"进行中" / "即将结束"）</summary>
            public string StatusText;

            /// <summary>是否为即将结束状态（决定状态标签配色：朱红）</summary>
            public bool Ending;

            /// <summary>插画字（设计稿 .illust-char）</summary>
            public string Illustration;

            /// <summary>剩余时间（设计稿 .card-meta .time-value）</summary>
            public string Time;

            /// <summary>两项奖励（设计稿 .reward-row）</summary>
            public ActivityRewardSpec[] Rewards;

            /// <summary>构造一张活动卡规格。</summary>
            public ActivityCardSpec(string name, string desc, string progressLabel, string progressValue,
                float progress, string statusText, bool ending, string illustration, string time,
                ActivityRewardSpec[] rewards)
            {
                Name = name;
                Desc = desc;
                ProgressLabel = progressLabel;
                ProgressValue = progressValue;
                Progress = progress;
                StatusText = statusText;
                Ending = ending;
                Illustration = illustration;
                Time = time;
                Rewards = rewards;
            }
        }

        /// <summary>6 张活动卡设计数据（顺序与设计稿 article 顺序一致）。</summary>
        private static readonly ActivityCardSpec[] _activitySpecs =
        {
            new ActivityCardSpec("每日签到", "签到打卡，连签7天获额外奖励", "连签进度", "0 / 7 天", 0f,
                "未开始", false, "签", "—",
                new[]
                {
                    new ActivityRewardSpec("×500", "铜钱", InkWashTheme.InkQuality.Common, "钱"),
                    new ActivityRewardSpec("×1", "玉色签到卡", InkWashTheme.InkQuality.Uncommon, "卡"),
                }),

            new ActivityCardSpec("闲庭信步", "在城镇中散步10分钟，感受江湖烟火", "散步里程", "0 / 10 公里", 0f,
                "未开始", false, "步", "—",
                new[]
                {
                    new ActivityRewardSpec("×2000", "经验", InkWashTheme.InkQuality.Rare, "验"),
                    new ActivityRewardSpec("×1", "轻功残页", InkWashTheme.InkQuality.Epic, "页"),
                }),

            new ActivityCardSpec("品茗对弈", "与NPC对弈一局，以棋会友", "对弈状态", "未开始", 0f,
                "未开始", false, "弈", "—",
                new[]
                {
                    new ActivityRewardSpec("×1", "棋谱", InkWashTheme.InkQuality.Rare, "谱"),
                    new ActivityRewardSpec("×50", "元宝", InkWashTheme.InkQuality.Epic, "宝"),
                }),

            new ActivityCardSpec("听书赏曲", "在茶馆听书30分钟，闲品江湖事", "聆听进度", "0 / 30 分钟", 0f,
                "未开始", false, "曲", "—",
                new[]
                {
                    new ActivityRewardSpec("×1500", "经验", InkWashTheme.InkQuality.Rare, "验"),
                    new ActivityRewardSpec("×300", "铜钱", InkWashTheme.InkQuality.Common, "钱"),
                }),

            new ActivityCardSpec("垂钓时光", "完成钓鱼3次，享受悠然午后", "钓鱼进度", "0 / 3 次", 0f,
                "未开始", false, "钓", "—",
                new[]
                {
                    new ActivityRewardSpec("×5", "鱼饵", InkWashTheme.InkQuality.Common, "饵"),
                    new ActivityRewardSpec("×3", "烹饪材料", InkWashTheme.InkQuality.Uncommon, "材"),
                }),

            new ActivityCardSpec("飞花令", "参与诗词小游戏，以诗会友", "吟诗进度", "未开始", 0f,
                "未开始", false, "花", "—",
                new[]
                {
                    new ActivityRewardSpec("×2", "诗词残卷", InkWashTheme.InkQuality.Rare, "卷"),
                    new ActivityRewardSpec("×30", "元宝", InkWashTheme.InkQuality.Epic, "宝"),
                }),
        };

        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

        public MenuCasualModePage()
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
                BuildBackButton();
                BuildLeftNav();
                BuildTopBar();
                BuildContentArea();
                BuildDailySummary();
                BuildTabBar();
                BuildActivityCards();
                BuildRewardPreviewBar();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuCasualModePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildBackButton()
        {
            _backButton = new InkButton
            {
                Width = 40f,
                Height = 40f,
                Text = "<",
                Variant = InkButtonVariant.Ghost,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };
            // 返回战斗 HUD（设计隐藏出口 back-hud）
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            var logoMark = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                Location = new Float2(16f, 20f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _leftNav
            };

            var logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.GoldBright,
                Size = new Float2(164f, 22f),
                Location = new Float2(60f, 20f),
                Parent = _leftNav
            };

            var logoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 8f),
                TextColor = InkWashTheme.TextDisabled,
                Size = new Float2(164f, 12f),
                Location = new Float2(60f, 42f),
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Location = new Float2(16f, 66f),
                Parent = _leftNav
            };

            string[] navItems = { "不肝", "抽卡", "活动", "通行证", "属性", "奇珍阁", "设置" };

            // 左侧导航即设计链路（批次5 §3.2）：不肝→抽卡→活动→通行证→属性→奇珍阁→设置
            string[] navTargets =
            {
                InkPageDomIds.NavCasualMode,
                InkPageDomIds.NavGacha,
                InkPageDomIds.NavActivities,
                InkPageDomIds.NavBattlePass,
                InkPageDomIds.NavCharacterV2,
                InkPageDomIds.NavShopRare,
                InkPageDomIds.NavSettings,
            };

            float navY = 78f;
            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 52f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(0f, navY),
                    Parent = _leftNav
                };

                if (i == 0)
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                    navItem.TextColor = InkWashTheme.GoldBright;
                }
                else
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.TextSecondary;
                }

                // 活动/通行证等待办数没有服务端来源：左栏不再挂写死的角标（原为 不肝 6 / 活动 3）

                // 左栏项直达对应菜单（设计链路）
                int navIndex = i;
                navItem.Clicked += () => NavigationRequested?.Invoke(navTargets[navIndex]);
                navY += 52f;
            }

            float navH = _screenSize.Y;
            var footerDivider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Location = new Float2(16f, navH - 70f),
                Parent = _leftNav
            };

            var serverDot = new ContainerControl
            {
                Size = new Float2(6f, 6f),
                Location = new Float2(16f, navH - 53f),
                BackgroundColor = InkWashTheme.JadeBright,
                Parent = _leftNav
            };

            var serverName = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Size = new Float2(196f, 16f),
                Location = new Float2(28f, navH - 59f),
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "混沌世界 v1.2.0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextDisabled,
                Size = new Float2(208f, 14f),
                Location = new Float2(16f, navH - 38f),
                Parent = _leftNav
            };
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _avatarBox = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _topBar
            };

            _charNameLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            _charLevelLabel = new Label
            {
                Text = "Lv.—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _charSchoolLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _vitalityBar = new InkBar
            {
                Height = 8f,
                Parent = _topBar
            };
            _vitalityBar.Value = 0f;

            _vitalityValueLabel = new Label
            {
                Text = "0/0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _currencyCoinLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _currencyIngotLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _topBar
            };

            _timePeriodLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _timeQuarterLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            // 真实玩家信息可用时覆盖设计稿示例值（门派/元宝/时辰暂无数据源）
            InkPlayerHeaderBinder.Bind(_charNameLabel, _charLevelLabel, _currencyCoinLabel);
            InkPlayerHeaderBinder.BindSect(_charSchoolLabel);
        }

        private void BuildContentArea()
        {
            _contentArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        private void BuildDailySummary()
        {
            _dailySummaryPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _contentArea
            };

            var cornerDecoTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _dailySummaryPanel
            };

            var cornerDecoTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _dailySummaryPanel
            };

            var cornerDecoBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _dailySummaryPanel
            };

            var cornerDecoBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _dailySummaryPanel
            };

            var summaryIcon = new ContainerControl
            {
                Size = new Float2(48f, 48f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _dailySummaryPanel
            };
            _summaryIcon = summaryIcon;

            _summaryLabel = new Label
            {
                Text = "今日活跃度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _dailySummaryPanel
            };

            _summaryValue = new Label
            {
                Text = "0 / 500",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _dailySummaryPanel
            };

            _activityBar = new InkBar
            {
                Height = 8f,
                Parent = _dailySummaryPanel
            };
            _activityBar.Value = 0f;

            var milestoneLabels = new[] { "100", "200", "300", "400", "500" };
            for (int i = 0; i < milestoneLabels.Length; i++)
            {
                var milestone = new Label
                {
                    Text = milestoneLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = _dailySummaryPanel
                };
                _milestoneLabels.Add(milestone);
            }

            int[] statNums = { 0, 0, 0 };
            string[] statLabels = { "已完成", "进行中", "待开始" };
            for (int i = 0; i < statNums.Length; i++)
            {
                var statNum = new Label
                {
                    Text = statNums[i].ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 20f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = _dailySummaryPanel
                };
                _statNumLabels.Add(statNum);

                var statLabel = new Label
                {
                    Text = statLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = _dailySummaryPanel
                };
                _statTextLabels.Add(statLabel);
            }
        }

        private void BuildTabBar()
        {
            string[] tabNames = { "推荐", "全部", "日常", "周常" };
            _tabButtons = new InkButton[tabNames.Length];

            for (int i = 0; i < tabNames.Length; i++)
            {
                _tabButtons[i] = new InkButton
                {
                    Width = 100f,
                    Height = 40f,
                    Text = tabNames[i],
                    Variant = InkButtonVariant.Ghost,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextDisabled,
                    Parent = _contentArea
                };
                if (i == 0)
                {
                    _tabButtons[i].BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                }
            }

            // 任务与奖励都是设计稿目录数据（没有服务端任务系统），在筛选条这一行如实标注
            _sampleNote = new Label
            {
                Text = "闲趣任务与奖励尚未接入服务端，以下为目录样例",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _contentArea
            };

            // 原来是局部变量且从未定位：一直贴在内容区左上角（0,0），压在活跃度面板上
            _filterButton = new InkButton
            {
                Width = 60f,
                Height = 28f,
                Text = "筛选",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextDisabled,
                Enabled = false, // 活动列表没有可筛的数据源
                Parent = _contentArea
            };
        }

        private void BuildActivityCards()
        {
            _activityCards = new ActivityCardView[_activitySpecs.Length];

            for (int i = 0; i < _activitySpecs.Length; i++)
            {
                var card = new ActivityCardView(_activitySpecs[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Parent = _contentArea
                };

                // 四角金线装饰（设计稿 .ink-corner-deco）
                var cornerDecoTL = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Parent = card
                };

                var cornerDecoTR = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Parent = card
                };

                var cornerDecoBL = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.BottomLeft,
                    Parent = card
                };

                var cornerDecoBR = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.BottomRight,
                    Parent = card
                };

                // 「前往」按钮直达对应活动入口（设计稿 .card-action-btn / btn-*）
                card.ActionClicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavActivities);

                _activityCards[i] = card;
            }
        }

        /// <summary>
        /// 活动卡视图。
        /// 对应设计稿 <c>.activity-card</c>：横向 flex 布局，左侧 64×64 插画块，
        /// 右侧 <c>.card-body</c> = 标题 + 状态标签 / 描述 / 进度标签行 + 进度条 /
        /// 底部奖励行（两项 .reward-item）+ 剩余时间与「前往」按钮。
        /// 子控件位置全部由 <see cref="LayoutCard"/> 按卡片实际尺寸计算。
        /// </summary>
        private sealed class ActivityCardView : InkPanel
        {
            /// <summary>卡片内边距（设计稿 .activity-card padding: 16px）</summary>
            private const float Padding = 16f;

            /// <summary>插画块边长（设计稿 .card-illustration 64px）</summary>
            private const float IllustrationSize = 64f;

            /// <summary>插画与内容列间距（设计稿 gap: 12px）</summary>
            private const float BodyGap = 12f;

            /// <summary>奖励图标方块边长（设计稿 .reward-icon 28px）</summary>
            private const float RewardIconSize = 28f;

            /// <summary>奖励项间距（设计稿 .reward-row gap: 12px）</summary>
            private const float RewardGap = 12f;

            /// <summary>奖励图标与文字间距（设计稿 .reward-item gap: 6px）</summary>
            private const float RewardTextGap = 6f;

            /// <summary>「前往」按钮尺寸（设计稿 .card-action-btn）</summary>
            private const float ActionButtonWidth = 68f;
            private const float ActionButtonHeight = 28f;

            /// <summary>状态标签宽度</summary>
            private const float StatusTagWidth = 72f;

            private readonly Label _title;
            private readonly Label _statusTag;
            private readonly Label _desc;
            private readonly Label _progressLabel;
            private readonly Label _progressValue;
            private readonly InkBar _progressBar;
            private readonly Panel _illustration;
            private readonly Label _illustChar;
            private readonly RewardIconBox[] _rewardIcons;
            private readonly Label[] _rewardQty;
            private readonly Label[] _rewardName;
            private readonly Label _timeLabel;
            private readonly InkButton _actionButton;

            /// <summary>「前往」按钮点击事件（由页面订阅后导航）。</summary>
            public event Action ActionClicked;

            /// <summary>
            /// 构造函数：按规格创建全部子控件（尺寸与位置在 <see cref="LayoutCard"/> 中统一计算）。
            /// </summary>
            /// <param name="spec">活动卡规格</param>
            public ActivityCardView(ActivityCardSpec spec)
            {
                BackgroundColor = InkWashTheme.BaseTertiary;
                ClipChildren = true;

                // 插画块（设计稿 .card-illustration：64px、金线描边、深墨底）
                _illustration = new Panel
                {
                    Size = new Float2(IllustrationSize, IllustrationSize),
                    BackgroundColor = InkWashTheme.GoldPrimary * 0.08f,
                    Parent = this
                };

                _illustChar = new Label
                {
                    Text = spec.Illustration,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    // 插画块 64×64：StretchAll 会被撑成 164×84，字偏出框外
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Vector2(IllustrationSize, IllustrationSize),
                    Parent = _illustration
                };

                // 标题 + 状态标签（设计稿 .card-header）
                _title = new Label
                {
                    Text = spec.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _statusTag = new Label
                {
                    Text = spec.StatusText,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = spec.Ending ? InkWashTheme.VermilionBright : InkWashTheme.QualityUncommon,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                // 描述（设计稿 .card-desc 12px 三级色）
                _desc = new Label
                {
                    Text = spec.Desc,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                // 进度标签行 + 进度条（设计稿 .card-progress）
                _progressLabel = new Label
                {
                    Text = spec.ProgressLabel,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _progressValue = new Label
                {
                    Text = spec.ProgressValue,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _progressBar = new InkBar
                {
                    Height = 4f,
                    Parent = this
                };
                _progressBar.Value = spec.Progress;

                // 奖励行（设计稿 .card-footer .reward-row，固定两项）
                int rewardCount = spec.Rewards != null ? spec.Rewards.Length : 0;
                _rewardIcons = new RewardIconBox[rewardCount];
                _rewardQty = new Label[rewardCount];
                _rewardName = new Label[rewardCount];

                for (int i = 0; i < rewardCount; i++)
                {
                    ActivityRewardSpec reward = spec.Rewards[i];

                    _rewardIcons[i] = new RewardIconBox(reward.Quality, reward.Glyph)
                    {
                        Parent = this
                    };

                    _rewardQty[i] = new Label
                    {
                        Text = reward.Qty,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                        TextColor = InkWashTheme.TextDefault,
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                        Parent = this
                    };

                    _rewardName[i] = new Label
                    {
                        Text = reward.Name,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                        TextColor = InkWashTheme.TextTertiary,
                        HorizontalAlignment = TextAlignment.Near,
                        VerticalAlignment = TextAlignment.Center,
                        Parent = this
                    };
                }

                // 剩余时间 + 「前往」按钮（设计稿 .card-meta）
                _timeLabel = new Label
                {
                    Text = spec.Time,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _actionButton = new InkButton
                {
                    Text = "前往",
                    // 设计方案：朱红仅限战斗/危险/扣减场景；休闲活动用 Primary 强调
                    Variant = InkButtonVariant.Primary,
                    Parent = this
                };
                _actionButton.Clicked += () => ActionClicked?.Invoke();
            }

            /// <summary>
            /// 按卡片实际尺寸排布全部子控件（浮动自适应，无硬编码坐标）。
            /// 应在卡片被赋予尺寸后（构建期与每次刷新布局时）调用。
            /// </summary>
            public void LayoutCard()
            {
                float w = Width;
                float h = Height;
                if (w <= 0f || h <= 0f)
                    return;

                // 左侧插画块
                _illustration.Location = new Float2(Padding, Padding);
                _illustration.Size = new Float2(IllustrationSize, IllustrationSize);

                // 右侧内容列
                float bodyX = Padding + IllustrationSize + BodyGap;
                float bodyW = w - bodyX - Padding;
                if (bodyW < 60f)
                    bodyW = 60f;

                _title.Location = new Float2(bodyX, Padding);
                _title.Size = new Float2(bodyW - StatusTagWidth - 8f, 22f);

                _statusTag.Location = new Float2(w - Padding - StatusTagWidth, Padding + 2f);
                _statusTag.Size = new Float2(StatusTagWidth, 18f);

                _desc.Location = new Float2(bodyX, Padding + 24f);
                _desc.Size = new Float2(bodyW, 18f);

                _progressLabel.Location = new Float2(bodyX, Padding + 46f);
                _progressLabel.Size = new Float2(bodyW * 0.5f, 14f);
                _progressValue.Location = new Float2(bodyX + bodyW * 0.5f, Padding + 46f);
                _progressValue.Size = new Float2(bodyW * 0.5f, 14f);

                _progressBar.Location = new Float2(bodyX, Padding + 64f);
                _progressBar.Size = new Float2(bodyW, 4f);

                // 底部：奖励行在上、剩余时间与按钮在下（设计稿 .card-footer margin-top: auto）
                float metaY = h - Padding - ActionButtonHeight;
                float rewardY = metaY - 8f - RewardIconSize;
                if (rewardY < Padding + 72f)
                    rewardY = Padding + 72f;

                float itemW = (bodyW - RewardGap) * 0.5f;
                for (int i = 0; i < _rewardIcons.Length; i++)
                {
                    float itemX = bodyX + i * (itemW + RewardGap);

                    _rewardIcons[i].Location = new Float2(itemX, rewardY);
                    _rewardIcons[i].Size = new Float2(RewardIconSize, RewardIconSize);

                    _rewardQty[i].Location = new Float2(itemX + RewardIconSize + RewardTextGap, rewardY + 1f);
                    _rewardQty[i].Size = new Float2(itemW - RewardIconSize - RewardTextGap, 14f);

                    _rewardName[i].Location = new Float2(itemX + RewardIconSize + RewardTextGap, rewardY + 15f);
                    _rewardName[i].Size = new Float2(itemW - RewardIconSize - RewardTextGap, 13f);
                }

                _timeLabel.Location = new Float2(bodyX, metaY);
                _timeLabel.Size = new Float2(bodyW - ActionButtonWidth - 8f, ActionButtonHeight);

                _actionButton.Location = new Float2(w - Padding - ActionButtonWidth, metaY);
                _actionButton.Size = new Float2(ActionButtonWidth, ActionButtonHeight);
            }
        }

        /// <summary>
        /// 奖励图标方块。
        /// 对应设计稿 <c>.reward-icon</c>：28×28、品质色 1px 描边、2px 圆角、深墨底，
        /// 内含图标字形（lucide 图标的引擎侧近似）。
        /// </summary>
        private sealed class RewardIconBox : ContainerControl
        {
            /// <summary>圆角半径（设计稿 var(--ink-radius-sm) = 2px）</summary>
            private const float CornerRadius = 2f;

            private readonly Color _borderColor;
            private Label _glyph;

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (_glyph != null)
                {
                    _glyph.Location = Float2.Zero;
                    _glyph.Size = Size;
                }
            }

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="quality">奖励品质（决定描边色）</param>
            /// <param name="glyph">图标字形</param>
            public RewardIconBox(InkWashTheme.InkQuality quality, string glyph)
            {
                BackgroundColor = new Color(0f, 0f, 0f, 0.3f);
                ClipChildren = true;
                AutoFocus = false;
                _borderColor = InkWashTheme.QualityColor(quality);

                var glyphLabel = new Label
                {
                    Text = glyph,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                    TextColor = _borderColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    // 奖励图标块尺寸由布局期给（28×28）：这里在 OnSizeChanged 里钉住
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Parent = this
                };
                _glyph = glyphLabel;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                base.Draw();

                if (Width <= 0f || Height <= 0f)
                    return;

                InkRenderHelper.DrawRoundedRectangle(
                    new Rectangle(0f, 0f, Width, Height), CornerRadius, _borderColor, 1f);
            }
        }

        private void BuildRewardPreviewBar()
        {
            _rewardPreviewBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = _contentArea
            };

            var cornerDecoTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _rewardPreviewBar
            };

            var cornerDecoTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _rewardPreviewBar
            };

            var cornerDecoBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _rewardPreviewBar
            };

            var cornerDecoBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _rewardPreviewBar
            };

            _previewTitle = new Label
            {
                Text = "今日奖励总览",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _rewardPreviewBar
            };

            _previewSubtitle = new Label
            {
                Text = "完成全部活动预计可获得",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _rewardPreviewBar
            };

            _previewItemCount = new Label
            {
                Text = "共 9 项奖励",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _rewardPreviewBar
            };

            _previewDetailButton = new InkButton
            {
                Width = 60f,
                Height = 28f,
                Text = "详情",
                Variant = InkButtonVariant.Ghost,
                Parent = _rewardPreviewBar
            };
            // 奖励总览的明细就是活动列表本身（奖励来自各活动），详情直接跳到活动页
            _previewDetailButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavActivities);

            string[] rewardNames = { "铜钱", "元宝", "经验", "玉色签到卡", "轻功残页", "棋谱", "鱼饵", "烹饪材料", "诗词残卷" };
            string[] rewardQty = { "×800", "×80", "×3500", "×1", "×1", "×1", "×5", "×3", "×2" };

            for (int i = 0; i < rewardNames.Length; i++)
            {
                var rewardName = new Label
                {
                    Text = rewardNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = _rewardPreviewBar
                };

                var rewardQtyLabel = new Label
                {
                    Text = rewardQty[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.GoldBright,
                    Parent = _rewardPreviewBar
                };
                _previewChips.Add((rewardName, rewardQtyLabel));
            }
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_backButton != null)
                _backButton.Location = new Float2(252f, 14f);

            if (_leftNav != null)
            {
                _leftNav.Size = new Float2(240f, sh);
                _leftNav.Location = new Float2(0f, 0f);
            }

            if (_topBar != null)
            {
                _topBar.Size = new Float2(sw - 240f, 60f);
                _topBar.Location = new Float2(240f, 0f);
            }

            if (_contentArea != null)
            {
                _contentArea.Size = new Float2(sw - 240f, sh - 60f);
                _contentArea.Location = new Float2(240f, 60f);
            }

            // 顶栏一行：头像 → 姓名/等级/门派 → 体力条 → 时辰/货币（靠右）
            float barW = sw - 240f;
            if (_avatarBox != null) _avatarBox.Bounds = new Rectangle(16f, 12f, 36f, 36f);
            if (_charNameLabel != null) _charNameLabel.Bounds = new Rectangle(64f, 8f, 140f, 20f);
            if (_charLevelLabel != null) _charLevelLabel.Bounds = new Rectangle(64f, 30f, 70f, 18f);
            if (_charSchoolLabel != null) _charSchoolLabel.Bounds = new Rectangle(140f, 30f, 90f, 18f);
            if (_vitalityBar != null) _vitalityBar.Bounds = new Rectangle(244f, 26f, 140f, 8f);
            if (_vitalityValueLabel != null) _vitalityValueLabel.Bounds = new Rectangle(392f, 20f, 70f, 18f);
            if (_timePeriodLabel != null) _timePeriodLabel.Bounds = new Rectangle(barW - 420f, 20f, 48f, 18f);
            if (_timeQuarterLabel != null) _timeQuarterLabel.Bounds = new Rectangle(barW - 366f, 20f, 40f, 18f);
            if (_currencyCoinLabel != null) _currencyCoinLabel.Bounds = new Rectangle(barW - 300f, 20f, 90f, 18f);
            if (_currencyIngotLabel != null) _currencyIngotLabel.Bounds = new Rectangle(barW - 190f, 20f, 80f, 18f);

            float padding = 24f;

            if (_dailySummaryPanel != null)
            {
                _dailySummaryPanel.Size = new Float2(sw - 240f - padding * 2f, 120f);
                _dailySummaryPanel.Location = new Float2(padding, padding);
            }

            float tabX = padding;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].Location = new Float2(tabX, 160f);
                    tabX += 100f;
                }
            }

            // 筛选按钮贴内容区右缘，样例说明放在它左边（避免与按钮重叠）
            float contentRight = padding + (sw - 240f - padding * 2f);
            if (_filterButton != null)
            {
                _filterButton.Location = new Float2(contentRight - 60f, 166f);
            }
            if (_sampleNote != null)
            {
                _sampleNote.Location = new Float2(contentRight - 60f - 16f - 320f, 160f);
                _sampleNote.Size = new Float2(320f, 40f);
            }

            float cardWidth = (sw - 240f - padding * 2f - 24f) / 3f;
            float cardHeight = 220f;
            float cardGridX = padding;
            float cardGridY = 220f;

            for (int i = 0; i < _activityCards.Length; i++)
            {
                if (_activityCards[i] != null)
                {
                    _activityCards[i].Size = new Float2(cardWidth, cardHeight);
                    _activityCards[i].Location = new Float2(cardGridX, cardGridY);

                    // 卡片内部为浮动布局：尺寸变化后必须重排子控件。
                    // 单卡异常不能带走后面的汇总/奖励条排布（那两块会停在 (0,0) 叠字）。
                    try
                    {
                        _activityCards[i].LayoutCard();
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[MenuCasualModePage] 活动卡#{i} 重排失败: {ex.Message}");
                    }
                }

                cardGridX += cardWidth + 12f;
                if ((i + 1) % 3 == 0)
                {
                    cardGridX = padding;
                    cardGridY += cardHeight + 16f;
                }
            }

            if (_rewardPreviewBar != null)
            {
                _rewardPreviewBar.Size = new Float2(sw - 240f - padding * 2f, 80f);
                _rewardPreviewBar.Location = new Float2(padding, cardGridY + 20f);
            }

            LayoutSummaryInner();
            LayoutRewardPreviewInner();
        }

        /// <summary>
        /// 活跃度汇总面板的内部排布（构造时这些子控件都没有位置，全部叠在 (0,0)）。
        /// 左：图标 + 标题 + 数值 + 进度条 + 刻度；右：已完成/进行中/待开始三组。
        /// </summary>
        private void LayoutSummaryInner()
        {
            if (_dailySummaryPanel == null)
                return;

            float w = _dailySummaryPanel.Size.X;

            if (_summaryIcon != null)
                _summaryIcon.Location = new Float2(24f, 36f);
            if (_summaryLabel != null)
            {
                _summaryLabel.Location = new Float2(88f, 30f);
                _summaryLabel.Size = new Float2(140f, 18f);
            }
            if (_summaryValue != null)
            {
                _summaryValue.Location = new Float2(88f, 50f);
                _summaryValue.Size = new Float2(160f, 26f);
            }

            float barX = 260f;
            float barW = Math.Max(120f, w - barX - 360f);
            if (_activityBar != null)
            {
                _activityBar.Location = new Float2(barX, 46f);
                _activityBar.Size = new Float2(barW, 8f);
            }

            for (int i = 0; i < _milestoneLabels.Count; i++)
            {
                float x = barX + barW * i / Math.Max(1, _milestoneLabels.Count - 1);
                _milestoneLabels[i].Location = new Float2(x - 20f, 62f);
                _milestoneLabels[i].Size = new Float2(40f, 14f);
            }

            for (int i = 0; i < _statNumLabels.Count; i++)
            {
                float x = w - 340f + i * 110f;
                _statNumLabels[i].Location = new Float2(x, 26f);
                _statNumLabels[i].Size = new Float2(90f, 26f);
                if (i < _statTextLabels.Count)
                {
                    _statTextLabels[i].Location = new Float2(x, 52f);
                    _statTextLabels[i].Size = new Float2(90f, 16f);
                }
            }
        }

        /// <summary>奖励总览条的内部排布：标题/副标题/数量在左，详情按钮在右，9 项奖励横排。</summary>
        private void LayoutRewardPreviewInner()
        {
            if (_rewardPreviewBar == null)
                return;

            float w = _rewardPreviewBar.Size.X;

            if (_previewTitle != null)
            {
                _previewTitle.Location = new Float2(24f, 8f);
                _previewTitle.Size = new Float2(180f, 20f);
            }
            if (_previewSubtitle != null)
            {
                _previewSubtitle.Location = new Float2(24f, 32f);
                _previewSubtitle.Size = new Float2(200f, 16f);
            }
            if (_previewItemCount != null)
            {
                _previewItemCount.Location = new Float2(232f, 32f);
                _previewItemCount.Size = new Float2(100f, 16f);
            }
            if (_previewDetailButton != null)
                _previewDetailButton.Location = new Float2(w - 84f, 26f);

            if (_previewChips.Count == 0)
                return;

            float chipAreaX = 360f;
            float chipAreaW = Math.Max(200f, w - chipAreaX - 110f);
            float chipW = chipAreaW / _previewChips.Count;
            for (int i = 0; i < _previewChips.Count; i++)
            {
                float x = chipAreaX + chipW * i;
                _previewChips[i].Name.Location = new Float2(x, 26f);
                _previewChips[i].Name.Size = new Float2(chipW - 4f, 16f);
                _previewChips[i].Qty.Location = new Float2(x, 46f);
                _previewChips[i].Qty.Size = new Float2(chipW - 4f, 16f);
            }
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
        }

        public void OnPageEnter()
        {
            RefreshAllData();
        }

        public void OnPageLeave()
        {
        }

        public void OnPageUpdate()
        {
        }

        public void OnResolutionChanged()
        {
            _screenSize = FlaxEngine.Screen.Size;
            ApplyLayout();
        }
    }
}

