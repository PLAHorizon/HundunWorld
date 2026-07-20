using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Casual
{
    public class MenuCasualModePage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkButton _backButton;
        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkPanel _contentArea;

        private InkPanel _dailySummaryPanel;
        private InkBar _vitalityBar;
        private InkBar _activityBar;

        private InkButton[] _tabButtons;

        private InkPanel[] _activityCards;

        private InkPanel _rewardPreviewBar;

        private string[] _activityNames = { "每日签到", "闲庭信步", "品茗对弈", "听书赏曲", "垂钓时光", "飞花令" };
        private string[] _activityDescs = {
            "签到打卡，连签7天获额外奖励",
            "在城镇中散步10分钟，感受江湖烟火",
            "与NPC对弈一局，以棋会友",
            "在茶馆听书30分钟，闲品江湖事",
            "完成钓鱼3次，享受悠然午后",
            "参与诗词小游戏，以诗会友"
        };
        private string[] _activityProgress = { "6 / 7 天", "4 / 10 分钟", "等待对手", "12 / 30 分钟", "1 / 3 次", "第 8 名" };
        private float[] _activityProgressValues = { 0.85f, 0.40f, 0.15f, 0.40f, 0.33f, 0.75f };
        private string[] _activityTimes = { "23:45:12", "无限制", "02:30:00", "05:15:30", "12:00:00", "18:22:45" };
        private string[] _activityIllustChars = { "签", "步", "弈", "曲", "钓", "花" };

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
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _leftNav
            };

            var logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _leftNav
            };

            var logoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 8f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Parent = _leftNav
            };

            string[] navItems = { "不肝", "抽卡", "活动", "通行证", "属性", "奇珍阁", "设置" };
            int[] badgeIndices = { 0, 2 };
            int[] badgeValues = { 6, 3 };

            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 52f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
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

                if (badgeIndices.Contains(i))
                {
                    int idx = Array.IndexOf(badgeIndices, i);
                    var badge = new Label
                    {
                        Text = badgeValues[idx].ToString(),
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                        TextColor = InkWashTheme.PaperBright,
                        Parent = navItem
                    };
                }
            }

            var footerDivider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Parent = _leftNav
            };

            var serverDot = new ContainerControl
            {
                Size = new Float2(6f, 6f),
                BackgroundColor = InkWashTheme.JadeBright,
                Parent = _leftNav
            };

            var serverName = new Label
            {
                Text = "江南 · 烟雨楼",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "v 2.4.1 · 烽火连城",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextDisabled,
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

            var avatarCircle = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _topBar
            };

            var charName = new Label
            {
                Text = "江湖过客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            var charLevel = new Label
            {
                Text = "Lv. 42",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var charSchool = new Label
            {
                Text = "无门无派",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _vitalityBar = new InkBar
            {
                Height = 8f,
                Parent = _topBar
            };
            _vitalityBar.Value = 120f / 180f;

            var vitalityValue = new Label
            {
                Text = "120/180",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var currencyCoin = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var currencyIngot = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _topBar
            };

            var timePeriod = new Label
            {
                Text = "戌时",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            var timeQuarter = new Label
            {
                Text = "三刻",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };
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

            var summaryLabel = new Label
            {
                Text = "今日活跃度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _dailySummaryPanel
            };

            var summaryValue = new Label
            {
                Text = "325 / 500",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _dailySummaryPanel
            };

            _activityBar = new InkBar
            {
                Height = 8f,
                Parent = _dailySummaryPanel
            };
            _activityBar.Value = 325f / 500f;

            var milestoneLabels = new[] { "100", "200", "300", "400", "500" };
            for (int i = 0; i < milestoneLabels.Length; i++)
            {
                var milestone = new Label
                {
                    Text = milestoneLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = i < 3 ? InkWashTheme.JadeBright : InkWashTheme.TextDisabled,
                    Parent = _dailySummaryPanel
                };
            }

            int[] statNums = { 2, 3, 1 };
            string[] statLabels = { "已完成", "进行中", "待开始" };
            for (int i = 0; i < statNums.Length; i++)
            {
                var statNum = new Label
                {
                    Text = statNums[i].ToString(),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 20f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = _dailySummaryPanel
                };

                var statLabel = new Label
                {
                    Text = statLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    Parent = _dailySummaryPanel
                };
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

            var filterButton = new InkButton
            {
                Width = 60f,
                Height = 28f,
                Text = "筛选",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextDisabled,
                Parent = _contentArea
            };
        }

        private void BuildActivityCards()
        {
            _activityCards = new InkPanel[_activityNames.Length];

            for (int i = 0; i < _activityNames.Length; i++)
            {
                _activityCards[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var cornerDecoTL = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Parent = _activityCards[i]
                };

                var cornerDecoTR = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Parent = _activityCards[i]
                };

                var cornerDecoBL = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.BottomLeft,
                    Parent = _activityCards[i]
                };

                var cornerDecoBR = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.BottomRight,
                    Parent = _activityCards[i]
                };

                var illustPanel = new InkPanel
                {
                    Size = new Float2(80f, 80f),
                    BackgroundColor = InkWashTheme.GoldPrimary * 0.08f,
                    Parent = _activityCards[i]
                };

                var illustChar = new Label
                {
                    Text = _activityIllustChars[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                    TextColor = InkWashTheme.GoldBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = illustPanel
                };

                var cardTitle = new Label
                {
                    Text = _activityNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = _activityCards[i]
                };

                var statusTag = new Label
                {
                    Text = i == 5 ? "即将结束" : "进行中",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = i == 5 ? InkWashTheme.VermilionBright : InkWashTheme.QualityUncommon,
                    Parent = _activityCards[i]
                };

                var cardDesc = new Label
                {
                    Text = _activityDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = _activityCards[i]
                };

                var progressLabel = new Label
                {
                    Text = _activityProgress[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    Parent = _activityCards[i]
                };

                var cardBar = new InkBar
                {
                    Height = 4f,
                    Parent = _activityCards[i]
                };
                cardBar.Value = _activityProgressValues[i];

                var timeLabel = new Label
                {
                    Text = _activityTimes[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextDisabled,
                    Parent = _activityCards[i]
                };

                var actionButton = new InkButton
                {
                    Width = 80f,
                    Height = 32f,
                    Text = "前往",
                    // 设计方案：朱红仅限战斗/危险/扣减场景；飞花令为休闲活动，用Primary强调
                    Variant = InkButtonVariant.Primary,
                    Parent = _activityCards[i]
                };
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

            var previewTitle = new Label
            {
                Text = "今日奖励总览",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _rewardPreviewBar
            };

            var previewSubtitle = new Label
            {
                Text = "完成全部活动预计可获得",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _rewardPreviewBar
            };

            var itemCount = new Label
            {
                Text = "共 8 项奖励",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _rewardPreviewBar
            };

            var detailButton = new InkButton
            {
                Width = 60f,
                Height = 28f,
                Text = "详情",
                Variant = InkButtonVariant.Ghost,
                Parent = _rewardPreviewBar
            };

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
