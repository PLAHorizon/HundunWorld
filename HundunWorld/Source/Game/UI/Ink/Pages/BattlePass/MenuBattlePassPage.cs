using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.BattlePass
{
    /// <summary>
    /// 江湖令 · 通行证页面。
    /// 逐条落地设计稿 hundun-yy-ui/pages/menu-battle-pass.html：
    /// 左侧导航栏（240px，含赛季进度）/ 顶部信息栏（60px）/
    /// 右侧滚动内容列（通行证头部 → 解锁横幅 → 等级轨道 → 当前等级奖励详情 → 赛季里程碑）。
    /// 颜色与字体一律取自 <see cref="InkWashTheme"/> 设计 Token（无硬编码色值），
    /// 导航出口经 <see cref="NavigationRequested"/> 交由 <see cref="InkPageRouter"/> 统一处理。
    /// </summary>
    public class MenuBattlePassPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ------------------------------------------------------------------
        // 布局常量（对应设计稿 1920x1080 参考分辨率）
        // ------------------------------------------------------------------

        /// <summary>左侧导航宽度（设计 240px）</summary>
        private const float SidebarWidth = 240f;

        /// <summary>顶部信息栏高度（设计 60px）</summary>
        private const float TopHeaderHeight = 60f;

        /// <summary>右侧内容区内边距（设计 --ink-space-5 = 24px）</summary>
        private const float ContentPadding = 24f;

        /// <summary>内容区块间距（设计 gap: --ink-space-4 = 16px）</summary>
        private const float SectionGap = 16f;

        /// <summary>通行证头部面板高度</summary>
        private const float PassHeaderHeight = 176f;

        /// <summary>解锁状态横幅高度</summary>
        private const float UnlockBannerHeight = 76f;

        /// <summary>等级轨道区块高度</summary>
        private const float TrackSectionHeight = 360f;

        /// <summary>当前等级奖励详情面板高度</summary>
        private const float DetailPanelHeight = 420f;

        /// <summary>赛季里程碑区块高度</summary>
        private const float MilestoneHeight = 176f;

        /// <summary>轨道行左侧标签列宽度</summary>
        private const float TrackRowLabelWidth = 48f;

        /// <summary>轨道节点尺寸</summary>
        private const float TrackNodeWidth = 96f;

        /// <summary>轨道节点高度</summary>
        private const float TrackNodeHeight = 108f;

        /// <summary>轨道节点间距（设计 --ink-space-2）</summary>
        private const float TrackNodeGap = 8f;

        // ------------------------------------------------------------------
        // 赛季示例数据（设计稿静态数据）
        // ------------------------------------------------------------------

        /// <summary>赛季标签文案</summary>
        private const string SeasonBadgeText = "第三赛季";

        /// <summary>赛季名称文案</summary>
        private const string SeasonNameText = "江湖行";

        /// <summary>顶栏中央赛季标题</summary>
        private const string SeasonTitleText = "第三赛季 · 江湖行";

        /// <summary>轨道起始等级（设计稿 Lv.20 段）</summary>
        private const int TrackStartLevel = 20;

        /// <summary>轨道等级段数量（设计稿 Lv.20 — Lv.29 共 10 级）</summary>
        private const int TrackLevelCount = 10;

        /// <summary>赛季总等级（设计稿侧栏 Lv.23 / Lv.50 的分母）</summary>
        private const int SeasonMaxLevel = 50;

        // ------------------------------------------------------------------
        // 组件初值 = 设计稿示例数据
        // 以下字段的初始值即 hundun-yy-ui/pages/menu-battle-pass.html 的示例数据，
        // 构造时直接写入各子控件；接入真实数据源后由本类「数据绑定 API」区的
        // Bind* 方法覆盖（未调用则始终保留设计稿初值）。
        // ------------------------------------------------------------------

        /// <summary>玩家等级（设计稿 Lv. 42）</summary>
        private int _playerLevel = -1;

        /// <summary>铜钱余额（设计稿 12,450）</summary>
        private int _coin = -1;

        /// <summary>元宝余额（设计稿 328）</summary>
        private int _ingot = -1;

        /// <summary>赛季剩余时间文案（设计稿 28天 06:30:00）</summary>
        private string _seasonRemainText = "—";

        /// <summary>赛季参与人数（设计稿 1,284,567）</summary>
        private int _seasonParticipants = 0;

        /// <summary>通行证当前等级（设计稿 Lv. 23）</summary>
        private int _passLevel = 0;

        /// <summary>当前等级已获经验（设计稿 1,950）</summary>
        private int _passExp = 0;

        /// <summary>当前等级升级所需经验（设计稿 3,000）</summary>
        private int _passExpRequired = 3000;

        /// <summary>本段已领取奖励数（设计稿 6）</summary>
        // 通行证没有服务端数据：已领取/可领取归零（2026-10-29）
        private int _claimedRewardCount = 0;

        /// <summary>本段可领取奖励数（设计稿 1）</summary>
        private int _claimableRewardCount = 0;

        /// <summary>本段未解锁奖励数（设计稿 13）</summary>
        private int _lockedRewardCount = 13;

        /// <summary>解锁至尊通行证价格（元宝，设计稿 680）</summary>
        private int _unlockPrice = 680;

        /// <summary>立即升级单价（元宝/级，设计稿 90）</summary>
        private int _upgradePricePerLevel = 90;

        /// <summary>侧栏赛季进度比例（通行证等级 / 赛季总等级，设计稿 23/50 ≈ 46%）。</summary>
        private float PassLevelRatio => Mathf.Clamp(_passLevel / (float)SeasonMaxLevel, 0f, 1f);

        /// <summary>当前等级升级进度比例（已获经验 / 所需经验，设计稿 1950/3000 = 65%）。</summary>
        private float PassExpRatio => _passExpRequired > 0
            ? Mathf.Clamp(_passExp / (float)_passExpRequired, 0f, 1f)
            : 0f;

        /// <summary>侧栏赛季进度文案（设计稿 Lv.23 / Lv.50）。</summary>
        private string PassLevelText => $"Lv.{_passLevel} / Lv.{SeasonMaxLevel}";

        /// <summary>当前等级升至下一级还差的经验（设计稿 1,050）。</summary>
        private int PassExpRemaining => System.Math.Max(0, _passExpRequired - _passExp);

        // ------------------------------------------------------------------
        // 轨道节点状态与数据规格
        // ------------------------------------------------------------------

        /// <summary>奖励节点领取状态。</summary>
        private enum NodeStatus
        {
            /// <summary>已领取</summary>
            Claimed,

            /// <summary>可领取</summary>
            Claimable,

            /// <summary>未解锁</summary>
            Locked
        }

        /// <summary>里程碑卡片状态。</summary>
        private enum MilestoneState
        {
            /// <summary>已解锁</summary>
            Claimed,

            /// <summary>进行中</summary>
            Current,

            /// <summary>未解锁</summary>
            Locked
        }

        /// <summary>单个轨道奖励节点数据规格。</summary>
        private sealed class RewardSpec
        {
            /// <summary>奖励名称</summary>
            public string Name;

            /// <summary>图标字符</summary>
            public string Glyph;

            /// <summary>品质</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>领取状态</summary>
            public NodeStatus Status;

            /// <summary>构造一条奖励规格。</summary>
            public RewardSpec(string name, string glyph, InkWashTheme.InkQuality quality, NodeStatus status)
            {
                Name = name;
                Glyph = glyph;
                Quality = quality;
                Status = status;
            }
        }

        /// <summary>赛季里程碑数据规格。</summary>
        private sealed class MilestoneSpec
        {
            /// <summary>解锁等级</summary>
            public int Level;

            /// <summary>奖励名称</summary>
            public string Name;

            /// <summary>图标字符</summary>
            public string Glyph;

            /// <summary>品质</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>卡片状态</summary>
            public MilestoneState State;

            /// <summary>状态文案</summary>
            public string StatusText;

            /// <summary>构造一条里程碑规格。</summary>
            public MilestoneSpec(int level, string name, string glyph, InkWashTheme.InkQuality quality, MilestoneState state, string statusText)
            {
                Level = level;
                Name = name;
                Glyph = glyph;
                Quality = quality;
                State = state;
                StatusText = statusText;
            }
        }

        /// <summary>至尊轨道奖励（Lv.20 — Lv.29）。</summary>
        private static readonly RewardSpec[] PaidRewards =
        {
            new RewardSpec("寒铁剑", "剑", InkWashTheme.InkQuality.Rare, NodeStatus.Locked),
            new RewardSpec("名侠碎片×3", "片", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
            new RewardSpec("元宝×200", "宝", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
            new RewardSpec("名侠碎片×5", "片", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
            new RewardSpec("玄铁护腕", "腕", InkWashTheme.InkQuality.Rare, NodeStatus.Locked),
            new RewardSpec("元宝×300", "宝", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
            new RewardSpec("名侠碎片×5", "片", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
            new RewardSpec("九转丹×3", "丹", InkWashTheme.InkQuality.Rare, NodeStatus.Locked),
            new RewardSpec("武学残卷", "卷", InkWashTheme.InkQuality.Legendary, NodeStatus.Locked),
            new RewardSpec("元宝×100", "宝", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
        };

        /// <summary>免费轨道奖励（Lv.20 — Lv.29）。</summary>
        private static readonly RewardSpec[] FreeRewards =
        {
            new RewardSpec("铜钱×500", "钱", InkWashTheme.InkQuality.Common, NodeStatus.Locked),
            new RewardSpec("疗伤丹×2", "丹", InkWashTheme.InkQuality.Uncommon, NodeStatus.Locked),
            new RewardSpec("元宝×50", "宝", InkWashTheme.InkQuality.Common, NodeStatus.Locked),
            new RewardSpec("铜钱×2000", "钱", InkWashTheme.InkQuality.Common, NodeStatus.Locked),
            new RewardSpec("疗伤丹×5", "丹", InkWashTheme.InkQuality.Uncommon, NodeStatus.Locked),
            new RewardSpec("元宝×80", "宝", InkWashTheme.InkQuality.Common, NodeStatus.Locked),
            new RewardSpec("精铁盾", "盾", InkWashTheme.InkQuality.Rare, NodeStatus.Locked),
            new RewardSpec("解毒丹×3", "丹", InkWashTheme.InkQuality.Uncommon, NodeStatus.Locked),
            new RewardSpec("铜钱×1500", "钱", InkWashTheme.InkQuality.Common, NodeStatus.Locked),
            new RewardSpec("元宝×100", "宝", InkWashTheme.InkQuality.Epic, NodeStatus.Locked),
        };

        /// <summary>赛季里程碑（Lv.10 / 20 / 30 / 40 / 50）。</summary>
        private static readonly MilestoneSpec[] Milestones =
        {
            new MilestoneSpec(10, "玄铁盾", "盾", InkWashTheme.InkQuality.Rare, MilestoneState.Claimed, "已解锁"),
            new MilestoneSpec(20, "寒铁剑", "剑", InkWashTheme.InkQuality.Epic, MilestoneState.Claimed, "已解锁"),
            new MilestoneSpec(30, "追风骏马", "马", InkWashTheme.InkQuality.Epic, MilestoneState.Current, "进行中"),
            new MilestoneSpec(40, "墨麟战甲", "甲", InkWashTheme.InkQuality.Legendary, MilestoneState.Locked, "未解锁"),
            new MilestoneSpec(50, "赛季冠军", "冠", InkWashTheme.InkQuality.Legendary, MilestoneState.Locked, "未解锁"),
        };

        /// <summary>快速获取 EXP 提示条目（设计稿 tips-list）。</summary>
        private static readonly string[] ExpTips =
        {
            "完成每日江湖历练 (+150 EXP)",
            "参与帮派活动 (+300 EXP)",
            "完成擂台挑战 (+200 EXP)",
            "直接使用元宝升级 (+∞ EXP)",
        };

        /// <summary>轨道图例条目（名称，圆点色，是否辉光）。</summary>
        private static readonly string[] LegendNames = { "至尊奖励", "免费奖励", "已领取", "可领取" };

        // ------------------------------------------------------------------
        // 页面根字段
        // ------------------------------------------------------------------

        private Float2 _screenSize;

        private Panel _backgroundLayer;
        private Panel _vignette;

        // 左侧导航栏
        private Panel _sidebar;
        private Label _sidebarSeal;
        private Label _sidebarLogo;
        private Label _sidebarLogoSub;
        private InkDivider _sidebarDivider;
        private InkDivider _sidebarFooterDivider;
        private InkSidebarList _sidebarList;
        private Label _sidebarSeasonGlyph;
        private Label _sidebarSeasonLabel;
        private Label _sidebarSeasonName;
        private InkBar _sidebarSeasonBar;
        private Label _sidebarSeasonProgress;

        // 顶部信息栏
        private Panel _mainArea;
        private Panel _topHeader;
        private InkButton _backButton;
        private InkDividerVertical _topHeaderDivider;
        private ContainerControl _playerAvatar;
        private Label _avatarLabel;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _topBarSeasonLabel;
        private Label _currencyCoinGlyph;
        private Label _currencyCoinLabel;
        private InkDividerVertical _currencyDividerA;
        private Label _currencyIngotGlyph;
        private Label _currencyIngotLabel;
        private InkDividerVertical _currencyDividerB;
        private Label _timeGlyph;
        private Label _timeLabel;

        // 右侧滚动内容列
        private Panel _rightContent;
        private Panel _contentColumn;

        // 通行证头部
        private InkPanel _passHeader;
        private InkCornerDeco _passHeaderCorners;
        private Label _passTitle;
        private InkTag _passSeasonBadge;
        private Label _passSubtitle;
        private Label _sampleNote;
        private Label _seasonTimeGlyph;
        private Label _seasonTimeLabel;
        private Label _seasonInfoTime;
        private Label _seasonPeopleGlyph;
        private Label _seasonPeopleLabel;
        private Label _seasonInfoPlayers;
        private Label _currentLevelCaption;
        private Label _currentLevelLabel;
        private Label _progressLabel;
        private Label _progressInfoLabel;
        private InkBar _progressBar;
        private Label _progressMarker;
        private Label _progressHint;
        private InkButton _unlockButton;
        private Label _unlockPriceLabel;
        private InkButton _upgradeButton;
        private Label _upgradePriceLabel;

        // 解锁状态横幅
        private InkPanel _unlockBanner;
        private Label _unlockGlyph;
        private Label _unlockTitle;
        private Label _unlockDesc;
        private List<QualityIcon> _unlockPreviewIcons;
        private List<Label> _unlockPreviewLabels;
        private InkButton _unlockActionButton;

        // 等级轨道
        private InkPanel _trackSection;
        private InkCornerDeco _trackCorners;
        private Label _trackTitle;
        private List<LegendItem> _trackLegend;
        private InkButton _trackPrevButton;
        private Label _trackRangeLabel;
        private InkButton _trackNextButton;
        private InkDivider _trackDivider;
        private InkDivider _trackFooterDivider;
        private Label _paidRowGlyph;
        private Label _paidRowLabel;
        private Label _freeRowGlyph;
        private Label _freeRowLabel;
        private List<TrackNode> _paidNodes;
        private List<TrackNode> _freeNodes;
        private List<Label> _levelNumberLabels;
        private Label _trackClaimedGlyph;
        private Label _trackClaimedText;
        private Label _trackClaimableGlyph;
        private Label _trackClaimableText;
        private Label _trackLockedGlyph;
        private Label _trackLockedText;
        private InkButton _claimAllButton;

        // 当前等级奖励详情
        private InkPanel _detailPanel;
        private InkCornerDeco _detailCorners;
        private Label _detailTitle;
        private InkTag _detailBadge;
        private Label _detailDesc;
        private InkDivider _detailDividerTop;
        private RewardDetailCard _freeRewardCard;
        private RewardDetailCard _paidRewardCard;
        private InkDivider _detailDividerBottom;
        private Label _nextLevelTitle;
        private Label _nextLevelHintGlyph;
        private Label _nextLevelHint;
        private Label _nextExpLabel;
        private Label _nextExpValue;
        private PreviewRewardCard _nextFreeCard;
        private PreviewRewardCard _nextPaidCard;
        private ExpTipsCard _expTipsCard;

        // 赛季里程碑
        private InkPanel _milestonePanel;
        private InkCornerDeco _milestoneCorners;
        private Label _milestoneTitle;
        private Label _milestoneSubtitle;
        private InkDivider _milestoneDivider;
        private List<MilestoneCard> _milestoneCards;

        /// <summary>
        /// 导航请求事件。由 <c>MainUIManager.HookNavigationRequested</c> 反射订阅后转交 <see cref="InkPageRouter"/>。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 构造函数：构建全部区块并应用布局。
        /// </summary>
        public MenuBattlePassPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(UILayout.RefWidth, UILayout.RefHeight);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildBackground();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildRightContent();
                BuildPassHeader();
                BuildUnlockBanner();
                BuildTrackSection();
                BuildDetailPanel();
                BuildMilestoneSection();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuBattlePassPage] 初始化失败: {ex.Message}");
            }
        }

        // ==================================================================
        // 通用辅助
        // ==================================================================

        /// <summary>
        /// 以既有 Token 派生同色系的半透明色（避免硬编码色值）。
        /// </summary>
        private static Color Alpha(Color token, float alpha)
        {
            return new Color(token.R, token.G, token.B, alpha);
        }

        /// <summary>
        /// 创建统一样式的文本控件。
        /// </summary>
        private static Label NewLabel(
            ContainerControl parent,
            string text,
            InkWashTheme.FontRole role,
            float size,
            Color color,
            TextAlignment alignment = TextAlignment.Near)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, size),
                TextColor = color,
                HorizontalAlignment = alignment,
                VerticalAlignment = TextAlignment.Center,
                Parent = parent,
            };
        }

        /// <summary>
        /// 创建水墨面板。
        /// </summary>
        private static InkPanel NewPanel(ContainerControl parent, Color background)
        {
            return new InkPanel
            {
                BackgroundColor = background,
                Parent = parent,
            };
        }

        /// <summary>
        /// 创建水墨按钮。
        /// </summary>
        private static InkButton NewButton(
            ContainerControl parent,
            string text,
            InkButtonVariant variant,
            InkButtonSize size = InkButtonSize.Md)
        {
            return new InkButton
            {
                Text = text,
                Variant = variant,
                ButtonSize = size,
                Parent = parent,
            };
        }

        /// <summary>
        /// 统一摆放控件的位置与尺寸。
        /// </summary>
        private static void Place(Control control, float x, float y, float width, float height)
        {
            if (control == null)
                return;
            control.Location = new Float2(x, y);
            control.Size = new Float2(width, height);
        }

        /// <summary>
        /// 创建一条竖向分割线并摆放。
        /// </summary>
        private static InkDividerVertical NewVerticalDivider(ContainerControl parent, float x, float y, float height)
        {
            var divider = new InkDividerVertical { Parent = parent };
            Place(divider, x, y, 1f, height);
            return divider;
        }

        // ==================================================================
        // 背景层
        // ==================================================================

        /// <summary>构建全局水墨背景层与暗角晕影。</summary>
        private void BuildBackground()
        {
            _backgroundLayer = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this,
            };

            _vignette = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(0f, 0f, 0f, 0.55f),
                Parent = this,
            };
        }

        // ==================================================================
        // 左侧导航栏
        // ==================================================================

        /// <summary>构建左侧导航栏：Logo、七个导航项、底部赛季进度。</summary>
        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this,
            };

            _sidebarSeal = NewLabel(_sidebar, "混", InkWashTheme.FontRole.Display, 22f, InkWashTheme.PaperBright, TextAlignment.Center);
            _sidebarSeal.BackgroundColor = Alpha(InkWashTheme.VermilionPrimary, 0.85f);

            _sidebarLogo = NewLabel(_sidebar, "混沌世界", InkWashTheme.FontRole.Display, 18f, InkWashTheme.GoldBright);
            _sidebarLogoSub = NewLabel(_sidebar, "江湖路远", InkWashTheme.FontRole.Heading, 11f, InkWashTheme.PaperDark);

            _sidebarDivider = new InkDivider { Parent = _sidebar };

            // 侧边栏导航：共用控件渲染 InkSidebarNav 的完整清单（战令项的"热"角标随条目走）。
            _sidebarList = new InkSidebarList(InkPageDomIds.NavBattlePass)
            {
                Parent = _sidebar,
            };
            _sidebarList.NavigationRequested += domId => NavigationRequested?.Invoke(domId);

            _sidebarFooterDivider = new InkDivider { Parent = _sidebar };

            _sidebarSeasonGlyph = NewLabel(_sidebar, "赏", InkWashTheme.FontRole.Display, 15f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _sidebarSeasonLabel = NewLabel(_sidebar, SeasonBadgeText, InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);
            _sidebarSeasonName = NewLabel(_sidebar, SeasonNameText, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.PaperBright);

            _sidebarSeasonBar = new InkBar
            {
                Height = 8f,
                FillVariant = InkBarFillVariant.Gold,
                Parent = _sidebar,
            };
            _sidebarSeasonBar.Value = PassLevelRatio;

            _sidebarSeasonProgress = NewLabel(_sidebar, PassLevelText, InkWashTheme.FontRole.Number, 11f, InkWashTheme.PaperDark, TextAlignment.Far);
        }

        // ==================================================================
        // 主区域与顶部信息栏
        // ==================================================================

        /// <summary>构建主内容区域容器。</summary>
        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                ClipChildren = true,
                Parent = this,
            };
        }

        /// <summary>构建顶部信息栏：返回战斗、角色信息、赛季标题、货币与时辰。</summary>
        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                Height = TopHeaderHeight,
                BackgroundColor = InkWashTheme.BaseSecondary,
                ClipChildren = false,
                Parent = _mainArea,
            };

            _backButton = NewButton(_topHeader, "← 返回战斗", InkButtonVariant.Ghost, InkButtonSize.Sm);
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            _topHeaderDivider = NewVerticalDivider(_topHeader, 0f, 0f, 24f);

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseTertiary,
                ClipChildren = false,
                Parent = _topHeader,
            };
            // 头像外圈金环（设计 .avatar-ring，向外扩 2px，先加入以置于文字下方）
            var avatarRing = new ContainerControl
            {
                Location = new Float2(-2f, -2f),
                Size = new Float2(40f, 40f),
                BackgroundColor = Alpha(InkWashTheme.GoldPrimary, 0.12f),
                ClipChildren = false,
                Parent = _playerAvatar,
            };
            _avatarLabel = NewLabel(_playerAvatar, "客", InkWashTheme.FontRole.Display, 16f, InkWashTheme.GoldPrimary, TextAlignment.Center);

            _playerNameLabel = NewLabel(_topHeader, "—", InkWashTheme.FontRole.Heading, 13f, InkWashTheme.PaperBright);
            _playerLevelLabel = NewLabel(_topHeader, _playerLevel >= 0 ? $"▲ Lv. {_playerLevel}" : "▲ Lv. —", InkWashTheme.FontRole.Number, 11f, InkWashTheme.GoldPrimary);
            _topBarSeasonLabel = NewLabel(_topHeader, SeasonTitleText, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.PaperDark, TextAlignment.Center);

            _currencyCoinGlyph = NewLabel(_topHeader, "钱", InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _currencyCoinLabel = NewLabel(_topHeader, _coin >= 0 ? $"铜钱 {_coin:N0}" : "铜钱 —", InkWashTheme.FontRole.Number, 13f, InkWashTheme.PaperBright);
            _currencyDividerA = NewVerticalDivider(_topHeader, 0f, 0f, 20f);
            _currencyIngotGlyph = NewLabel(_topHeader, "宝", InkWashTheme.FontRole.Body, 11f, InkWashTheme.VermilionBright, TextAlignment.Center);
            _currencyIngotLabel = NewLabel(_topHeader, _ingot >= 0 ? $"元宝 {_ingot:N0}" : "元宝 —", InkWashTheme.FontRole.Number, 13f, InkWashTheme.PaperBright);
            _currencyDividerB = NewVerticalDivider(_topHeader, 0f, 0f, 20f);
            _timeGlyph = NewLabel(_topHeader, "月", InkWashTheme.FontRole.Body, 11f, InkWashTheme.Info, TextAlignment.Center);
            // 世界时间同步没有接入客户端（同 MenuTimePage）
            _timeLabel = NewLabel(_topHeader, "—", InkWashTheme.FontRole.Heading, 13f, InkWashTheme.Info);

            // 真实玩家信息可用时覆盖设计稿示例值（元宝/时辰暂无数据源）
            InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelLabel, _currencyCoinLabel,
                levelPrefix: "▲ ", coinPrefix: "铜钱 ");
        }

        // ==================================================================
        // 右侧滚动内容列
        // ==================================================================

        /// <summary>构建右侧可滚动内容列容器。</summary>
        private void BuildRightContent()
        {
            _rightContent = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical,
                Parent = _mainArea,
            };

            _contentColumn = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Parent = _rightContent,
            };
        }

        // ==================================================================
        // 通行证头部
        // ==================================================================

        /// <summary>构建通行证头部：标题/赛季信息、当前等级与进度、解锁与升级操作。</summary>
        private void BuildPassHeader()
        {
            _passHeader = NewPanel(_contentColumn, InkWashTheme.Panel);
            _passHeaderCorners = new InkCornerDeco { Parent = _passHeader };

            _passTitle = NewLabel(_passHeader, "江湖令 · 通行证", InkWashTheme.FontRole.Display, 28f, InkWashTheme.GoldBright);
            _passSeasonBadge = new InkTag
            {
                Text = "赏 " + SeasonBadgeText,
                TagVariant = InkTagVariant.Brand,
                Parent = _passHeader,
            };
            _passSubtitle = NewLabel(_passHeader, "江湖行 · 纵马天涯，行侠仗义", InkWashTheme.FontRole.Heading, 13f, InkWashTheme.PaperAged);

            // 赛季等级/经验/奖励全部是设计稿示例初值，且没有可覆盖它们的服务端数据源
            _sampleNote = NewLabel(_passHeader, "赛季数据尚未接入 · 等级 / 经验 / 奖励均为示例",
                InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);

            _seasonTimeGlyph = NewLabel(_passHeader, "时", InkWashTheme.FontRole.Body, 11f, InkWashTheme.VermilionBright, TextAlignment.Center);
            _seasonTimeLabel = NewLabel(_passHeader, "剩余时间", InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);
            _seasonInfoTime = NewLabel(_passHeader, _seasonRemainText, InkWashTheme.FontRole.Number, 12f, InkWashTheme.VermilionBright);
            _seasonPeopleGlyph = NewLabel(_passHeader, "人", InkWashTheme.FontRole.Body, 11f, InkWashTheme.Info, TextAlignment.Center);
            _seasonPeopleLabel = NewLabel(_passHeader, "参与人数", InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);
            _seasonInfoPlayers = NewLabel(_passHeader, _seasonParticipants > 0 ? $"{_seasonParticipants:N0}" : "—", InkWashTheme.FontRole.Number, 12f, InkWashTheme.PaperBright);

            _currentLevelCaption = NewLabel(_passHeader, "当前等级", InkWashTheme.FontRole.Body, 12f, InkWashTheme.PaperDark);
            _currentLevelLabel = NewLabel(_passHeader, $"Lv. {_passLevel}", InkWashTheme.FontRole.Number, 28f, InkWashTheme.GoldBright);
            _progressLabel = NewLabel(_passHeader, "升级进度", InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);
            _progressInfoLabel = NewLabel(_passHeader, $"EXP {_passExp:N0} / {_passExpRequired:N0}", InkWashTheme.FontRole.Number, 12f, InkWashTheme.PaperAged, TextAlignment.Far);

            _progressBar = new InkBar
            {
                Height = 10f,
                FillVariant = InkBarFillVariant.Gold,
                Parent = _passHeader,
            };
            _progressBar.Value = PassExpRatio;

            _progressMarker = NewLabel(_passHeader, $"{Mathf.RoundToInt(PassExpRatio * 100f)}%", InkWashTheme.FontRole.Number, 10f, InkWashTheme.TextOnBrand, TextAlignment.Center);
            _progressMarker.BackgroundColor = InkWashTheme.GoldPrimary;

            _progressHint = NewLabel(_passHeader, $"再获得 {PassExpRemaining:N0} EXP 即可升至 Lv.{_passLevel + 1}", InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);

            _unlockButton = NewButton(_passHeader, "解锁至尊通行证", InkButtonVariant.Primary);
            _unlockButton.Clicked += OnUnlockClicked;
            _unlockPriceLabel = NewLabel(_passHeader, $"{_unlockPrice} 元宝", InkWashTheme.FontRole.Number, 11f, InkWashTheme.PaperAged, TextAlignment.Center);

            _upgradeButton = NewButton(_passHeader, "立即升级", InkButtonVariant.Vermilion);
            _upgradeButton.Clicked += OnUpgradeClicked;
            _upgradePriceLabel = NewLabel(_passHeader, $"{_upgradePricePerLevel} 元宝 / 级", InkWashTheme.FontRole.Number, 11f, InkWashTheme.PaperAged, TextAlignment.Center);
        }

        // ==================================================================
        // 解锁状态横幅
        // ==================================================================

        /// <summary>构建解锁状态横幅：未解锁提示、奖励预览图标、立即解锁按钮。</summary>
        private void BuildUnlockBanner()
        {
            _unlockBanner = NewPanel(_contentColumn, Alpha(InkWashTheme.GoldPrimary, 0.06f));

            _unlockGlyph = NewLabel(_unlockBanner, "锁", InkWashTheme.FontRole.Display, 20f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _unlockTitle = NewLabel(_unlockBanner, "您尚未解锁至尊通行证", InkWashTheme.FontRole.Heading, 15f, InkWashTheme.TextBrand);
            _unlockDesc = NewLabel(_unlockBanner, "解锁后可领取专属奖励，包含名侠碎片、稀有装备、限定外观等", InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark);

            _unlockPreviewIcons = new List<QualityIcon>();
            _unlockPreviewLabels = new List<Label>();
            string[] previewGlyphs = { "片", "衣", "剑" };
            string[] previewNames = { "名侠碎片", "限定外观", "稀有装备" };
            InkWashTheme.InkQuality[] previewQualities =
            {
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Rare,
            };
            for (int i = 0; i < previewGlyphs.Length; i++)
            {
                _unlockPreviewIcons.Add(new QualityIcon(_unlockBanner, 36f, previewGlyphs[i], previewQualities[i], false));
                _unlockPreviewLabels.Add(NewLabel(_unlockBanner, previewNames[i], InkWashTheme.FontRole.Body, 10f, InkWashTheme.PaperDark, TextAlignment.Center));
            }

            _unlockActionButton = NewButton(_unlockBanner, "立即解锁", InkButtonVariant.Primary);
            _unlockActionButton.Clicked += OnUnlockClicked;
        }

        // ==================================================================
        // 等级轨道
        // ==================================================================

        /// <summary>构建等级轨道区块：标题/图例/翻页、至尊与免费两条轨道、底部统计与一键领取。</summary>
        private void BuildTrackSection()
        {
            _trackSection = NewPanel(_contentColumn, InkWashTheme.Panel);
            _trackCorners = new InkCornerDeco { Parent = _trackSection };

            _trackTitle = NewLabel(_trackSection, "等级轨道", InkWashTheme.FontRole.Heading, 18f, InkWashTheme.PaperBright);

            _trackLegend = new List<LegendItem>();
            Color[] legendColors =
            {
                InkWashTheme.GoldPrimary,
                InkWashTheme.TextTertiary,
                InkWashTheme.JadePrimary,
                InkWashTheme.GoldBright,
            };
            for (int i = 0; i < LegendNames.Length; i++)
            {
                bool glow = i == LegendNames.Length - 1;
                _trackLegend.Add(new LegendItem(_trackSection, LegendNames[i], legendColors[i], glow));
            }

            _trackPrevButton = NewButton(_trackSection, "<", InkButtonVariant.Ghost, InkButtonSize.Sm);
            _trackRangeLabel = NewLabel(_trackSection, "Lv. 20 — Lv. 29", InkWashTheme.FontRole.Number, 13f, InkWashTheme.PaperBright, TextAlignment.Center);
            _trackNextButton = NewButton(_trackSection, ">", InkButtonVariant.Ghost, InkButtonSize.Sm);
            // 轨道只有设计稿这 10 个等级节点，没有别的分段可翻：原来两个按钮只是"重新打开本页"，
            // 点了画面没有任何变化。直接禁用，别做假交互。
            _trackPrevButton.Enabled = false;
            _trackNextButton.Enabled = false;

            _trackDivider = new InkDivider { Parent = _trackSection };

            _paidRowGlyph = NewLabel(_trackSection, "冠", InkWashTheme.FontRole.Body, 14f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _paidRowLabel = NewLabel(_trackSection, "至尊", InkWashTheme.FontRole.Heading, 11f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _freeRowGlyph = NewLabel(_trackSection, "礼", InkWashTheme.FontRole.Body, 14f, InkWashTheme.TextTertiary, TextAlignment.Center);
            _freeRowLabel = NewLabel(_trackSection, "免费", InkWashTheme.FontRole.Heading, 11f, InkWashTheme.TextTertiary, TextAlignment.Center);

            _paidNodes = new List<TrackNode>();
            for (int i = 0; i < PaidRewards.Length; i++)
            {
                RewardSpec spec = PaidRewards[i];
                bool current = TrackStartLevel + i == _passLevel && spec.Status == NodeStatus.Locked;
                _paidNodes.Add(new TrackNode(_trackSection, TrackStartLevel + i, spec, false, current));
            }

            _freeNodes = new List<TrackNode>();
            for (int i = 0; i < FreeRewards.Length; i++)
            {
                RewardSpec spec = FreeRewards[i];
                bool current = TrackStartLevel + i == _passLevel;
                _freeNodes.Add(new TrackNode(_trackSection, TrackStartLevel + i, spec, true, current));
            }

            _levelNumberLabels = new List<Label>();
            for (int i = 0; i < TrackLevelCount; i++)
            {
                int level = TrackStartLevel + i;
                Color numColor = level == _passLevel ? InkWashTheme.TextBrand : InkWashTheme.TextTertiary;
                _levelNumberLabels.Add(NewLabel(_trackSection, level.ToString(), InkWashTheme.FontRole.Number, 11f, numColor, TextAlignment.Center));
            }

            _trackFooterDivider = new InkDivider { Parent = _trackSection };

            _trackClaimedGlyph = NewLabel(_trackSection, "√", InkWashTheme.FontRole.Body, 11f, InkWashTheme.JadePrimary, TextAlignment.Center);
            _trackClaimedText = NewLabel(_trackSection, $"已领取: {_claimedRewardCount} / {TrackLevelCount}", InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperAged);
            _trackClaimableGlyph = NewLabel(_trackSection, "礼", InkWashTheme.FontRole.Body, 11f, InkWashTheme.GoldPrimary, TextAlignment.Center);
            _trackClaimableText = NewLabel(_trackSection, $"可领取: {_claimableRewardCount}", InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperAged);
            _trackLockedGlyph = NewLabel(_trackSection, "锁", InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary, TextAlignment.Center);
            _trackLockedText = NewLabel(_trackSection, $"未解锁: {_lockedRewardCount}", InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperAged);

            _claimAllButton = NewButton(_trackSection, "一键领取", InkButtonVariant.Primary);
            _claimAllButton.Clicked += OnClaimAllClicked;
        }

        // ==================================================================
        // 当前等级奖励详情
        // ==================================================================

        /// <summary>构建当前等级奖励详情：免费/至尊奖励卡、下一等级预告与快速获取 EXP 提示。</summary>
        private void BuildDetailPanel()
        {
            _detailPanel = NewPanel(_contentColumn, InkWashTheme.Panel);
            _detailCorners = new InkCornerDeco { Parent = _detailPanel };

            _detailTitle = NewLabel(_detailPanel, $"Lv. {_passLevel} 奖励详情", InkWashTheme.FontRole.Heading, 22f, InkWashTheme.PaperBright);
            _detailBadge = new InkTag
            {
                Text = "当前等级",
                TagVariant = InkTagVariant.Brand,
                Parent = _detailPanel,
            };
            _detailDesc = NewLabel(_detailPanel, "完成本等级所有奖励领取后，可继续通过获取EXP提升通行证等级", InkWashTheme.FontRole.Body, 13f, InkWashTheme.PaperDark);
            _detailDividerTop = new InkDivider { Parent = _detailPanel };

            _freeRewardCard = new RewardDetailCard(
                _detailPanel,
                false,
                "礼 免费奖励",
                "未解锁",
                "钱",
                InkWashTheme.InkQuality.Common,
                "铜钱 × 2000",
                "普通",
                InkWashTheme.TextTertiary,
                "货币",
                "江湖通用的铜钱货币，可在各类商铺购买基础物资",
                string.Empty);

            _paidRewardCard = new RewardDetailCard(
                _detailPanel,
                true,
                "冠 至尊奖励",
                "未解锁",
                "片",
                InkWashTheme.InkQuality.Epic,
                "名侠碎片 × 5",
                "史诗",
                InkWashTheme.TextBrand,
                "碎片",
                "收集名侠碎片可兑换江湖名侠的专属武学残卷，集齐50枚可召唤名侠",
                "解锁至尊通行证后可领取");
            _paidRewardCard.ClaimRequested += OnClaimRewardClicked;

            _detailDividerBottom = new InkDivider { Parent = _detailPanel };

            _nextLevelTitle = NewLabel(_detailPanel, $"Lv. {_passLevel + 1} 奖励预告", InkWashTheme.FontRole.Heading, 16f, InkWashTheme.PaperBright);
            _nextLevelHintGlyph = NewLabel(_detailPanel, "讯", InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary, TextAlignment.Center);
            _nextLevelHint = NewLabel(_detailPanel, $"升至 Lv.{_passLevel + 1} 后解锁", InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);
            _nextExpLabel = NewLabel(_detailPanel, "还需EXP", InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperDark, TextAlignment.Far);
            _nextExpValue = NewLabel(_detailPanel, $"{PassExpRemaining:N0}", InkWashTheme.FontRole.Number, 14f, InkWashTheme.TextVermilion, TextAlignment.Far);

            _nextFreeCard = new PreviewRewardCard(
                _detailPanel, "礼 免费", "丹", InkWashTheme.InkQuality.Uncommon, "疗伤丹 × 5", "稀有", InkWashTheme.JadePrimary, false);
            _nextPaidCard = new PreviewRewardCard(
                _detailPanel, "冠 至尊", "衣", InkWashTheme.InkQuality.Legendary, "限定外观", "传世", InkWashTheme.TextVermilion, true);
            _expTipsCard = new ExpTipsCard(_detailPanel, "快速获取EXP", ExpTips);
        }

        // ==================================================================
        // 赛季里程碑
        // ==================================================================

        /// <summary>构建赛季里程碑五档卡片。</summary>
        private void BuildMilestoneSection()
        {
            _milestonePanel = NewPanel(_contentColumn, InkWashTheme.Panel);
            _milestoneCorners = new InkCornerDeco { Parent = _milestonePanel };

            _milestoneTitle = NewLabel(_milestonePanel, "赛季里程碑", InkWashTheme.FontRole.Heading, 18f, InkWashTheme.PaperBright);
            _milestoneSubtitle = NewLabel(_milestonePanel, "达到指定等级解锁专属赛季奖励", InkWashTheme.FontRole.Body, 12f, InkWashTheme.PaperDark);
            _milestoneDivider = new InkDivider { Parent = _milestonePanel };

            _milestoneCards = new List<MilestoneCard>();
            for (int i = 0; i < Milestones.Length; i++)
            {
                _milestoneCards.Add(new MilestoneCard(_milestonePanel, Milestones[i]));
            }
        }

        // ==================================================================
        // 布局
        // ==================================================================

        /// <summary>按当前屏幕尺寸重排全部区块。</summary>
        private void ApplyLayout()
        {
            float w = _screenSize.X;
            float h = _screenSize.Y;
            if (w <= 0f)
                w = UILayout.RefWidth;
            if (h <= 0f)
                h = UILayout.RefHeight;

            LayoutSidebar(h);

            float mainW = Mathf.Max(360f, w - SidebarWidth);
            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0f);
                _mainArea.Size = new Float2(mainW, h);
            }

            LayoutTopHeader(mainW);
            LayoutContent(mainW, Mathf.Max(0f, h - TopHeaderHeight));
        }

        /// <summary>左侧导航栏布局。</summary>
        private void LayoutSidebar(float height)
        {
            if (_sidebar == null)
                return;

            Place(_sidebar, 0f, 0f, SidebarWidth, height);
            Place(_sidebarSeal, 16f, 16f, 40f, 40f);
            Place(_sidebarLogo, 64f, 16f, 160f, 20f);
            Place(_sidebarLogoSub, 64f, 38f, 160f, 16f);
            Place(_sidebarDivider, 16f, 70f, SidebarWidth - 32f, 1f);

            float navTop = 84f;
            const float navItemHeight = 44f;
            _sidebarList.Layout(0f, navTop, SidebarWidth, Mathf.Max(0f, height - navTop - 96f));


            float footerTop = height - 96f;
            Place(_sidebarFooterDivider, 16f, footerTop, SidebarWidth - 32f, 1f);
            Place(_sidebarSeasonGlyph, 16f, footerTop + 12f, 20f, 20f);
            Place(_sidebarSeasonLabel, 44f, footerTop + 10f, 160f, 16f);
            Place(_sidebarSeasonName, 44f, footerTop + 26f, 160f, 18f);
            Place(_sidebarSeasonBar, 16f, footerTop + 52f, SidebarWidth - 32f, 8f);
            Place(_sidebarSeasonProgress, 16f, footerTop + 64f, SidebarWidth - 32f, 16f);
        }

        /// <summary>顶部信息栏布局。</summary>
        private void LayoutTopHeader(float mainW)
        {
            if (_topHeader == null)
                return;

            Place(_topHeader, 0f, 0f, mainW, TopHeaderHeight);

            float centerY = TopHeaderHeight * 0.5f;
            Place(_backButton, 16f, centerY - 14f, 110f, 28f);
            Place(_topHeaderDivider, 138f, centerY - 12f, 1f, 24f);
            Place(_playerAvatar, 154f, centerY - 18f, 36f, 36f);
            Place(_avatarLabel, 0f, 0f, 36f, 36f);
            Place(_playerNameLabel, 198f, centerY - 16f, 140f, 16f);
            Place(_playerLevelLabel, 198f, centerY + 2f, 140f, 16f);
            Place(_topBarSeasonLabel, mainW * 0.5f - 110f, centerY - 9f, 220f, 18f);

            float rightEdge = mainW - 20f;
            float timeW = 92f;
            float glyphSize = 16f;
            float labelW = 84f;
            float dividerGap = 14f;

            Place(_timeLabel, rightEdge - timeW, centerY - 9f, timeW, 18f);
            Place(_timeGlyph, rightEdge - timeW - glyphSize - 2f, centerY - 8f, glyphSize, 16f);
            float cursor = rightEdge - timeW - glyphSize - 2f;
            Place(_currencyDividerB, cursor - dividerGap, centerY - 10f, 1f, 20f);
            cursor -= dividerGap + 1f;
            Place(_currencyIngotLabel, cursor - labelW, centerY - 9f, labelW, 18f);
            cursor -= labelW;
            Place(_currencyIngotGlyph, cursor - glyphSize, centerY - 8f, glyphSize, 16f);
            cursor -= glyphSize + 2f;
            Place(_currencyDividerA, cursor - dividerGap, centerY - 10f, 1f, 20f);
            cursor -= dividerGap + 1f;
            Place(_currencyCoinLabel, cursor - timeW, centerY - 9f, timeW, 18f);
            cursor -= timeW;
            Place(_currencyCoinGlyph, cursor - glyphSize, centerY - 8f, glyphSize, 16f);
        }

        /// <summary>右侧滚动内容列布局。</summary>
        private void LayoutContent(float mainW, float viewHeight)
        {
            float contentW = Mathf.Max(600f, mainW - ContentPadding * 2f - 14f);
            float contentHeight = ContentPadding
                + PassHeaderHeight + SectionGap
                + UnlockBannerHeight + SectionGap
                + TrackSectionHeight + SectionGap
                + DetailPanelHeight + SectionGap
                + MilestoneHeight
                + ContentPadding;

            Place(_rightContent, 0f, TopHeaderHeight, mainW, viewHeight);
            Place(_contentColumn, ContentPadding, ContentPadding, contentW, contentHeight);

            float y = 0f;
            Place(_passHeader, 0f, y, contentW, PassHeaderHeight);
            LayoutPassHeader(contentW);
            y += PassHeaderHeight + SectionGap;

            Place(_unlockBanner, 0f, y, contentW, UnlockBannerHeight);
            LayoutUnlockBanner(contentW);
            y += UnlockBannerHeight + SectionGap;

            Place(_trackSection, 0f, y, contentW, TrackSectionHeight);
            LayoutTrackSection(contentW);
            y += TrackSectionHeight + SectionGap;

            Place(_detailPanel, 0f, y, contentW, DetailPanelHeight);
            LayoutDetailPanel(contentW);
            y += DetailPanelHeight + SectionGap;

            Place(_milestonePanel, 0f, y, contentW, MilestoneHeight);
            LayoutMilestone(contentW);
        }

        /// <summary>通行证头部内部布局。</summary>
        private void LayoutPassHeader(float w)
        {
            float pad = ContentPadding;
            Place(_passHeaderCorners, 0f, 0f, w, PassHeaderHeight);

            // 左列：标题 / 赛季徽章 / 副标题 / 赛季信息
            Place(_passTitle, pad, 22f, 280f, 34f);
            Place(_passSeasonBadge, pad + 288f, 28f, 96f, 24f);
            Place(_passSubtitle, pad, 60f, 420f, 20f);
            Place(_sampleNote, pad, 146f, 420f, 16f);
            Place(_seasonTimeGlyph, pad, 92f, 14f, 14f);
            Place(_seasonTimeLabel, pad + 18f, 90f, 60f, 18f);
            Place(_seasonInfoTime, pad + 82f, 90f, 140f, 18f);
            Place(_seasonPeopleGlyph, pad, 114f, 14f, 14f);
            Place(_seasonPeopleLabel, pad + 18f, 112f, 60f, 18f);
            Place(_seasonInfoPlayers, pad + 82f, 112f, 140f, 18f);

            // 中列：当前等级 / 升级进度 / 进度条 / 提示
            float centerX = w * 0.36f;
            float centerW = Mathf.Max(240f, w * 0.26f);
            Place(_currentLevelCaption, centerX, 46f, 80f, 18f);
            Place(_currentLevelLabel, centerX + 84f, 30f, 160f, 38f);
            Place(_progressLabel, centerX, 78f, 70f, 18f);
            Place(_progressInfoLabel, centerX + 70f, 78f, centerW - 70f, 18f);
            Place(_progressBar, centerX, 100f, centerW, 10f);
            Place(_progressMarker, centerX + centerW * PassExpRatio - 22f, 96f, 44f, 18f);
            Place(_progressHint, centerX, 124f, centerW + 120f, 18f);

            // 右列：解锁 / 升级操作与价格
            float colW = 240f;
            float rightX = w - pad - colW;
            Place(_unlockButton, rightX, 30f, colW, 40f);
            Place(_unlockPriceLabel, rightX, 74f, colW, 18f);
            Place(_upgradeButton, rightX, 98f, colW, 40f);
            Place(_upgradePriceLabel, rightX, 142f, colW, 18f);
        }

        /// <summary>解锁横幅内部布局。</summary>
        private void LayoutUnlockBanner(float w)
        {
            float pad = ContentPadding;
            float centerY = UnlockBannerHeight * 0.5f;
            Place(_unlockGlyph, pad, centerY - 15f, 30f, 30f);
            Place(_unlockTitle, pad + 44f, centerY - 20f, 380f, 20f);
            Place(_unlockDesc, pad + 44f, centerY + 2f, 520f, 18f);

            float btnW = 140f;
            float btnX = w - pad - btnW;
            Place(_unlockActionButton, btnX, centerY - 20f, btnW, 40f);

            float previewX = btnX - 16f - 36f;
            for (int i = _unlockPreviewIcons.Count - 1; i >= 0; i--)
            {
                QualityIcon icon = _unlockPreviewIcons[i];
                Place(icon, previewX, centerY - 24f, 36f, 36f);
                Place(_unlockPreviewLabels[i], previewX - 6f, centerY + 12f, 48f, 14f);
                previewX -= 36f + 6f;
            }
        }

        /// <summary>等级轨道内部布局。</summary>
        private void LayoutTrackSection(float w)
        {
            float pad = ContentPadding;
            Place(_trackCorners, 0f, 0f, w, TrackSectionHeight);

            Place(_trackTitle, pad, 16f, 120f, 26f);
            float legendX = pad + 132f;
            for (int i = 0; i < _trackLegend.Count; i++)
            {
                _trackLegend[i].LayoutItem(legendX, 21f, 96f);
                legendX += 104f;
            }

            float navRight = w - pad;
            Place(_trackNextButton, navRight - 28f, 18f, 28f, 28f);
            Place(_trackRangeLabel, navRight - 28f - 8f - 140f, 18f, 140f, 28f);
            Place(_trackPrevButton, navRight - 28f - 8f - 140f - 8f - 28f, 18f, 28f, 28f);

            Place(_trackDivider, pad, 56f, w - pad * 2f, 1f);

            float nodesAreaX = pad + TrackRowLabelWidth;
            float nodesAreaW = w - pad * 2f - TrackRowLabelWidth;
            float nodesW = TrackNodeWidth * TrackLevelCount + TrackNodeGap * (TrackLevelCount - 1);
            float startX = nodesAreaX + Mathf.Max(0f, (nodesAreaW - nodesW) * 0.5f);

            float paidY = 68f;
            Place(_paidRowGlyph, pad, paidY + 28f, TrackRowLabelWidth, 20f);
            Place(_paidRowLabel, pad, paidY + 50f, TrackRowLabelWidth, 18f);
            for (int i = 0; i < _paidNodes.Count; i++)
            {
                _paidNodes[i].LayoutNode(startX + i * (TrackNodeWidth + TrackNodeGap), paidY, TrackNodeWidth);
            }

            float numbersY = paidY + TrackNodeHeight + 4f;
            for (int i = 0; i < _levelNumberLabels.Count; i++)
            {
                Place(_levelNumberLabels[i], startX + i * (TrackNodeWidth + TrackNodeGap), numbersY, TrackNodeWidth, 20f);
            }

            float freeY = numbersY + 24f;
            Place(_freeRowGlyph, pad, freeY + 28f, TrackRowLabelWidth, 20f);
            Place(_freeRowLabel, pad, freeY + 50f, TrackRowLabelWidth, 18f);
            for (int i = 0; i < _freeNodes.Count; i++)
            {
                _freeNodes[i].LayoutNode(startX + i * (TrackNodeWidth + TrackNodeGap), freeY, TrackNodeWidth);
            }

            float footerDividerY = freeY + TrackNodeHeight + 8f;
            Place(_trackFooterDivider, pad, footerDividerY, w - pad * 2f, 1f);

            float footerY = footerDividerY + 8f;
            Place(_trackClaimedGlyph, pad, footerY + 6f, 16f, 16f);
            Place(_trackClaimedText, pad + 20f, footerY + 4f, 180f, 20f);
            Place(_trackClaimableGlyph, pad + 210f, footerY + 6f, 16f, 16f);
            Place(_trackClaimableText, pad + 230f, footerY + 4f, 160f, 20f);
            Place(_trackLockedGlyph, pad + 400f, footerY + 6f, 16f, 16f);
            Place(_trackLockedText, pad + 420f, footerY + 4f, 160f, 20f);
            Place(_claimAllButton, w - pad - 130f, footerY, 130f, 32f);
        }

        /// <summary>奖励详情面板内部布局。</summary>
        private void LayoutDetailPanel(float w)
        {
            float pad = ContentPadding;
            Place(_detailCorners, 0f, 0f, w, DetailPanelHeight);

            Place(_detailTitle, pad, 18f, 260f, 32f);
            Place(_detailBadge, pad + 268f, 22f, 92f, 24f);
            Place(_detailDesc, pad, 56f, w - pad * 2f, 20f);
            Place(_detailDividerTop, pad, 84f, w - pad * 2f, 1f);

            float cardW = (w - pad * 2f - 16f) * 0.5f;
            const float cardH = 160f;
            Place(_freeRewardCard, pad, 96f, cardW, cardH);
            _freeRewardCard.LayoutCard(cardW, cardH);
            Place(_paidRewardCard, pad + cardW + 16f, 96f, cardW, cardH);
            _paidRewardCard.LayoutCard(cardW, cardH);

            Place(_detailDividerBottom, pad, 268f, w - pad * 2f, 1f);

            float previewY = 280f;
            Place(_nextLevelTitle, pad, previewY, 180f, 24f);
            Place(_nextLevelHintGlyph, pad + 190f, previewY + 4f, 14f, 14f);
            Place(_nextLevelHint, pad + 208f, previewY + 2f, 180f, 20f);
            Place(_nextExpLabel, w - pad - 180f, previewY + 4f, 80f, 18f);
            Place(_nextExpValue, w - pad - 96f, previewY, 96f, 24f);

            float gridY = 312f;
            const float gridH = 96f;
            float gridW = w - pad * 2f;
            float smallW = Mathf.Max(160f, gridW * 0.2f);
            float tipsW = gridW - smallW * 2f - 24f;

            Place(_nextFreeCard, pad, gridY, smallW, gridH);
            _nextFreeCard.LayoutCard(smallW, gridH);
            Place(_nextPaidCard, pad + smallW + 12f, gridY, smallW, gridH);
            _nextPaidCard.LayoutCard(smallW, gridH);
            Place(_expTipsCard, pad + smallW * 2f + 24f, gridY, tipsW, gridH);
            _expTipsCard.LayoutCard(tipsW, gridH);
        }

        /// <summary>赛季里程碑内部布局。</summary>
        private void LayoutMilestone(float w)
        {
            float pad = ContentPadding;
            Place(_milestoneCorners, 0f, 0f, w, MilestoneHeight);

            Place(_milestoneTitle, pad, 16f, 140f, 26f);
            Place(_milestoneSubtitle, pad + 148f, 20f, 300f, 20f);
            Place(_milestoneDivider, pad, 52f, w - pad * 2f, 1f);

            float cardW = (w - pad * 2f - 12f * 4f) / 5f;
            const float cardH = 100f;
            for (int i = 0; i < _milestoneCards.Count; i++)
            {
                float x = pad + i * (cardW + 12f);
                Place(_milestoneCards[i], x, 64f, cardW, cardH);
                _milestoneCards[i].LayoutCard(cardW, cardH);
            }
        }

        // ==================================================================
        // 数据刷新与交互出口
        // ==================================================================

        /// <summary>
        /// 把赛季数据写入各显示控件。
        /// 数据来源为实例字段（初值 = 设计稿示例数据，可被「数据绑定 API」区的 Bind* 方法覆盖），
        /// 本方法在构造末尾与每次绑定后调用，保证控件显示与字段始终一致。
        /// </summary>
        private void RefreshAllData()
        {
            RefreshAccountData();
            RefreshSeasonData();
            RefreshPassProgressData();
        }

        /// <summary>刷新顶栏账号数据（等级与货币余额）。</summary>
        private void RefreshAccountData()
        {
            if (_playerLevelLabel != null)
            {
                // 顶栏这一格是「玩家等级」，不是通行证等级（通行证等级在同页 `_passLevel`）。
                // 先取真实属性；拿不到再退回页面自己收到的账号数据，最后才是占位。
                InkPlayerHeaderBinder.BindLevel(_playerLevelLabel, "▲ ");
                if (_playerLevelLabel.Text == "▲ Lv. —" && _playerLevel >= 0)
                    _playerLevelLabel.Text = $"▲ Lv. {_playerLevel}";
            }
            if (_currencyCoinLabel != null)
                _currencyCoinLabel.Text = _coin >= 0 ? $"铜钱 {_coin:N0}" : "铜钱 —";
            if (_currencyIngotLabel != null)
                _currencyIngotLabel.Text = $"元宝 {_ingot:N0}";
        }

        /// <summary>刷新赛季信息（剩余时间/参与人数）与侧栏赛季进度。</summary>
        private void RefreshSeasonData()
        {
            if (_seasonInfoTime != null)
                _seasonInfoTime.Text = _seasonRemainText;
            if (_seasonInfoPlayers != null)
                _seasonInfoPlayers.Text = _seasonParticipants > 0 ? $"{_seasonParticipants:N0}" : "—";
            if (_sidebarSeasonBar != null)
                _sidebarSeasonBar.Value = PassLevelRatio;
            if (_sidebarSeasonProgress != null)
                _sidebarSeasonProgress.Text = PassLevelText;
        }

        /// <summary>刷新通行证等级/升级进度/价格/奖励统计与详情面板等级文案。</summary>
        private void RefreshPassProgressData()
        {
            if (_progressBar != null)
                _progressBar.Value = PassExpRatio;
            if (_progressMarker != null)
                _progressMarker.Text = Mathf.RoundToInt(PassExpRatio * 100f) + "%";
            if (_progressInfoLabel != null)
                _progressInfoLabel.Text = $"EXP {_passExp:N0} / {_passExpRequired:N0}";
            if (_progressHint != null)
                _progressHint.Text = $"再获得 {PassExpRemaining:N0} EXP 即可升至 Lv.{_passLevel + 1}";
            if (_currentLevelLabel != null)
                _currentLevelLabel.Text = $"Lv. {_passLevel}";
            if (_detailTitle != null)
                _detailTitle.Text = $"Lv. {_passLevel} 奖励详情";
            if (_nextLevelTitle != null)
                _nextLevelTitle.Text = $"Lv. {_passLevel + 1} 奖励预告";
            if (_nextLevelHint != null)
                _nextLevelHint.Text = $"升至 Lv.{_passLevel + 1} 后解锁";
            if (_nextExpValue != null)
                _nextExpValue.Text = $"{PassExpRemaining:N0}";
            if (_unlockPriceLabel != null)
                _unlockPriceLabel.Text = $"{_unlockPrice} 元宝";
            if (_upgradePriceLabel != null)
                _upgradePriceLabel.Text = $"{_upgradePricePerLevel} 元宝 / 级";
            if (_trackClaimedText != null)
                _trackClaimedText.Text = $"已领取: {_claimedRewardCount} / {TrackLevelCount}";
            if (_trackClaimableText != null)
                _trackClaimableText.Text = $"可领取: {_claimableRewardCount}";
            if (_trackLockedText != null)
                _trackLockedText.Text = $"未解锁: {_lockedRewardCount}";
            if (_trackRangeLabel != null)
            {
                _trackRangeLabel.Text = "Lv. " + TrackStartLevel + " — Lv. " + (TrackStartLevel + TrackLevelCount - 1);
            }
        }

        // ==================================================================
        // 数据绑定 API（接入真实数据源时覆盖设计稿示例初值）
        //
        // 页面构造即已按设计稿示例数据填充全部控件；以下方法供数据源接入后调用，
        // 每次调用都会写入对应字段并立即刷新控件显示。不调用则始终保留设计稿初值，
        // 便于在设计比对与无数据场景下验证页面外观。
        // ==================================================================

        /// <summary>
        /// 绑定玩家账号数据：等级与货币余额（顶栏）。
        /// </summary>
        /// <param name="playerLevel">玩家等级</param>
        /// <param name="coin">铜钱余额</param>
        /// <param name="ingot">元宝余额</param>
        public void BindAccount(int playerLevel, int coin, int ingot)
        {
            _playerLevel = playerLevel;
            _coin = coin;
            _ingot = ingot;
            RefreshAccountData();
        }

        /// <summary>
        /// 绑定赛季信息：剩余时间文案与参与人数。
        /// </summary>
        /// <param name="remainText">剩余时间文案，如 "28天 06:30:00"</param>
        /// <param name="participants">参与人数</param>
        public void BindSeasonInfo(string remainText, int participants)
        {
            _seasonRemainText = remainText ?? string.Empty;
            _seasonParticipants = participants;
            RefreshSeasonData();
        }

        /// <summary>
        /// 绑定通行证等级与升级进度：等级、当前经验、升级所需经验与奖励领取统计。
        /// 进度条、百分比、"再获得 x EXP" 与详情面板等级文案同步刷新。
        /// </summary>
        /// <param name="passLevel">通行证当前等级</param>
        /// <param name="expCurrent">当前等级已获经验</param>
        /// <param name="expRequired">升级所需经验（≤0 时进度按 0 处理）</param>
        /// <param name="claimedCount">本段已领取奖励数</param>
        /// <param name="claimableCount">本段可领取奖励数</param>
        /// <param name="lockedCount">本段未解锁奖励数</param>
        public void BindPassProgress(
            int passLevel,
            int expCurrent,
            int expRequired,
            int claimedCount,
            int claimableCount,
            int lockedCount)
        {
            _passLevel = passLevel;
            _passExp = expCurrent;
            _passExpRequired = expRequired;
            _claimedRewardCount = claimedCount;
            _claimableRewardCount = claimableCount;
            _lockedRewardCount = lockedCount;
            RefreshSeasonData();
            RefreshPassProgressData();
        }

        /// <summary>
        /// 绑定通行证价格：解锁至尊通行证价格与立即升级单价（元宝）。
        /// </summary>
        /// <param name="unlockPrice">解锁至尊通行证价格（元宝）</param>
        /// <param name="upgradePricePerLevel">立即升级单价（元宝/级）</param>
        public void BindPassPrices(int unlockPrice, int upgradePricePerLevel)
        {
            _unlockPrice = unlockPrice;
            _upgradePricePerLevel = upgradePricePerLevel;
            RefreshPassProgressData();
        }

        /// <summary>解锁至尊通行证（头部主按钮与横幅按钮共用出口）。</summary>
        private void OnUnlockClicked()
        {
            NavigationRequested?.Invoke(InkPageDomIds.NavShopRare);
        }

        /// <summary>立即升级：跳转奇珍阁补充元宝后升级。</summary>
        private void OnUpgradeClicked()
        {
            NavigationRequested?.Invoke(InkPageDomIds.NavShopRare);
        }

        /// <summary>领取当前等级至尊奖励：通行证没有服务端领取接口，给明确提示（原来只是重开本页）。</summary>
        private void OnClaimRewardClicked()
        {
            HundunWorld.Game.UI.UIHelper.ShowToast("通行证奖励领取尚未接入服务端",
                HundunWorld.Game.UI.ToastType.Info);
        }

        /// <summary>一键领取全部可领取奖励：同上。</summary>
        private void OnClaimAllClicked()
        {
            HundunWorld.Game.UI.UIHelper.ShowToast("通行证奖励领取尚未接入服务端",
                HundunWorld.Game.UI.ToastType.Info);
        }



        /// <summary>按当前控件尺寸重排布局（<see cref="IInkPage"/> 契约）。</summary>
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        // ==================================================================
        // 私有嵌套控件
        // ==================================================================

        /// <summary>
        /// 品质图标。圆形（轨道节点）或圆角方形（卡片）描边 + 图标字符，
        /// 颜色取自 <see cref="InkWashTheme.QualityColor"/> 等既有 Token。
        /// </summary>
        private sealed class QualityIcon : ContainerControl
        {
            private readonly Label _glyphLabel;
            private readonly InkWashTheme.InkQuality _quality;
            private readonly bool _circle;
            private readonly bool _highlight;

            /// <summary>构造品质图标。</summary>
            public QualityIcon(
                ContainerControl parent,
                float size,
                string glyph,
                InkWashTheme.InkQuality quality,
                bool circle,
                bool highlight = false)
            {
                Size = new Float2(size, size);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _quality = quality;
                _circle = circle;
                _highlight = highlight;

                Color glyphColor = highlight ? InkWashTheme.GoldBright : InkWashTheme.QualityColor(quality);
                _glyphLabel = new Label
                {
                    Text = glyph,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, size * 0.46f),
                    TextColor = glyphColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(size, size),
                    Parent = this,
                };

                Parent = parent;
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (_glyphLabel != null)
                    _glyphLabel.Size = Size;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                Color qualityColor = InkWashTheme.QualityColor(_quality);
                Color borderColor = _highlight ? InkWashTheme.GoldBright : qualityColor;
                Color fillColor = Alpha(qualityColor, _highlight ? 0.16f : 0.08f);
                var bounds = new Rectangle(0f, 0f, Width, Height);

                if (_circle)
                {
                    float radius = Mathf.Min(Width, Height) * 0.5f;
                    var center = new Float2(Width * 0.5f, Height * 0.5f);
                    InkRenderHelper.FillCircle(center, radius, fillColor);
                    InkRenderHelper.DrawCircle(center, radius - 1f, borderColor, 2f);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, fillColor);
                    InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, borderColor, 2f);
                }

                base.Draw();
            }
        }

        /// <summary>节点右上角领取状态圆徽。</summary>
        private sealed class StatusBadge : ContainerControl
        {
            private readonly Label _glyphLabel;
            private Color _fillColor = InkWashTheme.JadePrimary;

            /// <summary>构造状态徽章。</summary>
            public StatusBadge(ContainerControl parent, float size)
            {
                Size = new Float2(size, size);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _glyphLabel = new Label
                {
                    Text = "√",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, size * 0.6f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Size = new Float2(size, size),
                    Parent = this,
                };

                Parent = parent;
            }

            /// <summary>按领取状态切换徽章配色与字形。</summary>
            public void Set(NodeStatus status)
            {
                switch (status)
                {
                    case NodeStatus.Locked:
                        _fillColor = InkWashTheme.BaseElevated;
                        _glyphLabel.Text = "锁";
                        _glyphLabel.TextColor = InkWashTheme.TextTertiary;
                        break;

                    default:
                        _fillColor = InkWashTheme.JadePrimary;
                        _glyphLabel.Text = "√";
                        _glyphLabel.TextColor = InkWashTheme.PaperBright;
                        break;
                }
            }

            /// <inheritdoc />
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (_glyphLabel != null)
                    _glyphLabel.Size = Size;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float radius = Mathf.Min(Width, Height) * 0.5f;
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                InkRenderHelper.FillCircle(center, radius, _fillColor);
                InkRenderHelper.DrawCircle(center, radius - 0.5f, Alpha(InkWashTheme.BaseDefault, 0.9f), 1f);
                base.Draw();
            }
        }

        /// <summary>等级轨道上的单个奖励节点。</summary>
        private sealed class TrackNode : ContainerControl
        {
            private const float IconSize = 48f;
            private const float BadgeWidth = 52f;

            private readonly Label _levelLabel;
            private readonly Label _nameLabel;
            private readonly Label _claimableBadge;
            private readonly QualityIcon _icon;
            private readonly StatusBadge _status;
            private readonly bool _levelBelow;

            /// <summary>构造轨道节点。</summary>
            public TrackNode(ContainerControl parent, int level, RewardSpec spec, bool levelBelow, bool current)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _levelBelow = levelBelow;

                _claimableBadge = new Label
                {
                    Text = "可领取",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 9f),
                    TextColor = InkWashTheme.TextOnBrand,
                    BackgroundColor = InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Visible = spec.Status == NodeStatus.Locked,
                    Parent = this,
                };

                _levelLabel = NewLabel(
                    this,
                    "Lv." + level,
                    InkWashTheme.FontRole.Number,
                    11f,
                    current ? InkWashTheme.TextBrand : InkWashTheme.PaperDark,
                    TextAlignment.Center);

                _icon = new QualityIcon(this, IconSize, spec.Glyph, spec.Quality, true, current);

                _status = new StatusBadge(this, 18f);
                _status.Set(spec.Status);

                _nameLabel = NewLabel(
                    this,
                    spec.Name,
                    InkWashTheme.FontRole.Body,
                    10f,
                    current ? InkWashTheme.GoldPrimary : InkWashTheme.PaperDark,
                    TextAlignment.Center);

                Parent = parent;
            }

            /// <summary>摆放节点（x/y 为节点左上角，宽高由常量决定）。</summary>
            public void LayoutNode(float x, float y, float width)
            {
                Location = new Float2(x, y);
                Size = new Float2(width, TrackNodeHeight);

                float iconX = (width - IconSize) * 0.5f;
                float iconY = _levelBelow ? 18f : 36f;

                Place(_claimableBadge, (width - BadgeWidth) * 0.5f, 0f, BadgeWidth, 16f);
                Place(_icon, iconX, iconY, IconSize, IconSize);
                Place(_status, iconX + IconSize - 14f, iconY - 4f, 18f, 18f);

                if (_levelBelow)
                {
                    Place(_nameLabel, 0f, iconY + IconSize + 4f, width, 16f);
                    Place(_levelLabel, 0f, iconY + IconSize + 24f, width, 16f);
                }
                else
                {
                    Place(_levelLabel, 0f, 18f, width, 16f);
                    Place(_nameLabel, 0f, iconY + IconSize + 2f, width, 16f);
                }
            }
        }

        /// <summary>轨道标题栏图例条目（圆点 + 文案）。</summary>
        private sealed class LegendItem : ContainerControl
        {
            private readonly Label _textLabel;
            private readonly Color _dotColor;
            private readonly bool _glow;

            /// <summary>构造图例条目。</summary>
            public LegendItem(ContainerControl parent, string text, Color dotColor, bool glow)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _dotColor = dotColor;
                _glow = glow;

                _textLabel = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this,
                };

                Parent = parent;
            }

            /// <summary>摆放图例条目。</summary>
            public void LayoutItem(float x, float y, float width)
            {
                Location = new Float2(x, y);
                Size = new Float2(width, 16f);
                Place(_textLabel, 14f, 0f, Mathf.Max(0f, width - 14f), 16f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var center = new Float2(5f, Height * 0.5f);
                if (_glow)
                    InkRenderHelper.FillCircle(center, 7f, Alpha(_dotColor, 0.35f));
                InkRenderHelper.FillCircle(center, 5f, _dotColor);
                base.Draw();
            }
        }

        /// <summary>当前等级奖励详情卡（免费 / 至尊两态）。</summary>
        private sealed class RewardDetailCard : ContainerControl
        {
            private readonly bool _paid;
            private readonly Label _trackLabel;
            private readonly InkTag _statusTag;
            private readonly QualityIcon _icon;
            private readonly Label _nameLabel;
            private readonly Label _qualityCaption;
            private readonly Label _qualityValue;
            private readonly Label _typeCaption;
            private readonly Label _typeValue;
            private readonly Label _descLabel;
            private readonly Label _lockGlyph;
            private readonly Label _lockHint;
            private readonly InkButton _actionButton;

            /// <summary>领取请求事件。</summary>
            public event Action ClaimRequested;

            /// <summary>构造奖励详情卡。</summary>
            public RewardDetailCard(
                ContainerControl parent,
                bool paid,
                string trackLabel,
                string statusText,
                string glyph,
                InkWashTheme.InkQuality quality,
                string rewardName,
                string qualityText,
                Color qualityColor,
                string typeText,
                string description,
                string lockHint)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _paid = paid;

                _trackLabel = NewLabel(
                    this,
                    trackLabel,
                    InkWashTheme.FontRole.Heading,
                    13f,
                    paid ? InkWashTheme.TextBrand : InkWashTheme.PaperAged);

                _statusTag = new InkTag
                {
                    Text = statusText,
                    TagVariant = paid ? InkTagVariant.Brand : InkTagVariant.Default,
                    Parent = this,
                };

                _icon = new QualityIcon(this, 64f, glyph, quality, false, paid);
                _nameLabel = NewLabel(this, rewardName, InkWashTheme.FontRole.Heading, 15f, paid ? InkWashTheme.TextBrand : InkWashTheme.PaperBright);
                _qualityCaption = NewLabel(this, "品质:", InkWashTheme.FontRole.Body, 12f, InkWashTheme.PaperDark);
                _qualityValue = NewLabel(this, qualityText, InkWashTheme.FontRole.Body, 12f, qualityColor);
                _typeCaption = NewLabel(this, "类型:", InkWashTheme.FontRole.Body, 12f, InkWashTheme.PaperDark);
                _typeValue = NewLabel(this, typeText, InkWashTheme.FontRole.Body, 12f, InkWashTheme.PaperAged);
                _descLabel = NewLabel(this, description, InkWashTheme.FontRole.Body, 12f, InkWashTheme.TextTertiary);
                _descLabel.Wrapping = TextWrapping.WrapWords;

                _lockGlyph = NewLabel(this, "锁", InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary, TextAlignment.Center);
                _lockHint = NewLabel(this, lockHint, InkWashTheme.FontRole.Body, 11f, InkWashTheme.TextTertiary);

                if (paid)
                {
                    _actionButton = NewButton(this, "领取奖励", InkButtonVariant.Primary);
                    _actionButton.Clicked += () => ClaimRequested?.Invoke();
                }
                else
                {
                    // 免费奖励没有服务端领取记录：按等级锁态显示，不再假装"已领取"
                    _actionButton = NewButton(this, "未解锁", InkButtonVariant.Ghost);
                    _actionButton.Enabled = false;
                }

                Parent = parent;
            }

            /// <summary>摆放卡片内部控件。</summary>
            public void LayoutCard(float width, float height)
            {
                float pad = 14f;
                Place(_trackLabel, pad, 12f, 130f, 20f);
                Place(_statusTag, width - pad - 84f, 12f, 84f, 20f);
                Place(_icon, pad, 42f, 64f, 64f);
                Place(_nameLabel, pad + 76f, 40f, Mathf.Max(80f, width - pad * 2f - 76f), 22f);
                Place(_qualityCaption, pad + 76f, 64f, 40f, 18f);
                Place(_qualityValue, pad + 118f, 64f, 60f, 18f);
                Place(_typeCaption, pad + 186f, 64f, 40f, 18f);
                Place(_typeValue, pad + 228f, 64f, 60f, 18f);
                Place(_descLabel, pad + 76f, 84f, Mathf.Max(80f, width - pad * 2f - 76f), 36f);

                float buttonW = 110f;
                Place(_actionButton, width - pad - buttonW, height - 38f, buttonW, 30f);
                Place(_lockGlyph, pad, height - 32f, 14f, 14f);
                Place(_lockHint, pad + 18f, height - 34f, Mathf.Max(60f, width - pad * 3f - buttonW - 18f), 18f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                Color border = _paid ? InkWashTheme.BorderGold : InkWashTheme.BorderNeutralL1;
                Color background = _paid
                    ? Alpha(InkWashTheme.GoldPrimary, 0.04f)
                    : Alpha(InkWashTheme.Abyss, 0.5f);
                var bounds = new Rectangle(0f, 0f, Width, Height);

                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, background);
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, border, 1f);
                base.Draw();
            }
        }

        /// <summary>下一等级预告奖励小卡。</summary>
        private sealed class PreviewRewardCard : ContainerControl
        {
            private readonly bool _paid;
            private readonly Label _trackTag;
            private readonly QualityIcon _icon;
            private readonly Label _nameLabel;
            private readonly Label _qualityLabel;

            /// <summary>构造预告小卡。</summary>
            public PreviewRewardCard(
                ContainerControl parent,
                string trackTag,
                string glyph,
                InkWashTheme.InkQuality quality,
                string rewardName,
                string qualityText,
                Color qualityColor,
                bool paid)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _paid = paid;

                _trackTag = NewLabel(
                    this,
                    trackTag,
                    InkWashTheme.FontRole.Body,
                    11f,
                    paid ? InkWashTheme.TextBrand : InkWashTheme.PaperDark,
                    TextAlignment.Center);

                _icon = new QualityIcon(this, 40f, glyph, quality, false, paid);
                _nameLabel = NewLabel(
                    this,
                    rewardName,
                    InkWashTheme.FontRole.Heading,
                    13f,
                    paid ? InkWashTheme.TextBrand : InkWashTheme.PaperBright,
                    TextAlignment.Center);
                _qualityLabel = NewLabel(this, qualityText, InkWashTheme.FontRole.Body, 11f, qualityColor, TextAlignment.Center);

                Parent = parent;
            }

            /// <summary>摆放小卡内部控件。</summary>
            public void LayoutCard(float width, float height)
            {
                Place(_trackTag, 0f, 6f, width, 16f);
                Place(_icon, (width - 40f) * 0.5f, 24f, 40f, 40f);
                Place(_nameLabel, 0f, 66f, width, 16f);
                Place(_qualityLabel, 0f, 82f, width, 14f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                Color border = _paid ? InkWashTheme.BorderGold : InkWashTheme.BorderNeutralL1;
                Color background = _paid
                    ? Alpha(InkWashTheme.GoldPrimary, 0.04f)
                    : Alpha(InkWashTheme.Abyss, 0.5f);
                var bounds = new Rectangle(0f, 0f, Width, Height);

                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, background);
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, border, 1f);
                base.Draw();
            }
        }

        /// <summary>快速获取 EXP 提示卡。</summary>
        private sealed class ExpTipsCard : ContainerControl
        {
            private readonly List<Label> _tipLabels;

            /// <summary>构造提示卡。</summary>
            public ExpTipsCard(ContainerControl parent, string title, string[] tips)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                NewLabel(this, "◆ " + title, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.TextBrand);

                _tipLabels = new List<Label>();
                for (int i = 0; i < tips.Length; i++)
                {
                    _tipLabels.Add(NewLabel(this, "◆ " + tips[i], InkWashTheme.FontRole.Body, 11f, InkWashTheme.PaperAged));
                }

                Parent = parent;
            }

            /// <summary>摆放提示卡内部控件。</summary>
            public void LayoutCard(float width, float height)
            {
                float pad = 12f;
                float labelW = Mathf.Max(80f, width - pad * 2f);
                Place(Children[0], pad, 6f, labelW, 18f);
                for (int i = 0; i < _tipLabels.Count; i++)
                {
                    Place(_tipLabels[i], pad, 28f + i * 16f, labelW, 16f);
                }
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, Alpha(InkWashTheme.GoldPrimary, 0.04f));
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, InkWashTheme.BorderGold, 1f);
                base.Draw();
            }
        }

        /// <summary>赛季里程碑卡片。</summary>
        private sealed class MilestoneCard : ContainerControl
        {
            private readonly MilestoneState _state;
            private readonly Label _levelLabel;
            private readonly QualityIcon _icon;
            private readonly Label _nameLabel;
            private readonly Label _statusLabel;

            /// <summary>构造里程碑卡片。</summary>
            public MilestoneCard(ContainerControl parent, MilestoneSpec spec)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                _state = spec.State;

                Color levelColor = spec.State == MilestoneState.Current ? InkWashTheme.TextBrand : InkWashTheme.PaperAged;
                _levelLabel = NewLabel(this, "Lv. " + spec.Level, InkWashTheme.FontRole.Number, 13f, levelColor, TextAlignment.Center);
                _icon = new QualityIcon(this, 32f, spec.Glyph, spec.Quality, false, spec.State == MilestoneState.Current);
                _nameLabel = NewLabel(this, spec.Name, InkWashTheme.FontRole.Heading, 13f, InkWashTheme.PaperBright, TextAlignment.Center);

                Color statusColor;
                string statusGlyph;
                switch (spec.State)
                {
                    case MilestoneState.Current:
                        statusColor = InkWashTheme.TextBrand;
                        statusGlyph = "时";
                        break;

                    case MilestoneState.Locked:
                        statusColor = InkWashTheme.TextTertiary;
                        statusGlyph = "锁";
                        break;

                    default:
                        statusColor = InkWashTheme.JadePrimary;
                        statusGlyph = "√";
                        break;
                }

                _statusLabel = NewLabel(this, statusGlyph + " " + spec.StatusText, InkWashTheme.FontRole.Body, 11f, statusColor, TextAlignment.Center);

                Parent = parent;
            }

            /// <summary>摆放卡片内部控件。</summary>
            public void LayoutCard(float width, float height)
            {
                Place(_levelLabel, 0f, 6f, width, 18f);
                Place(_icon, (width - 32f) * 0.5f, 26f, 32f, 32f);
                Place(_nameLabel, 0f, 60f, width, 18f);
                Place(_statusLabel, 0f, 78f, width, 18f);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                Color border;
                Color background;
                switch (_state)
                {
                    case MilestoneState.Current:
                        border = InkWashTheme.GoldPrimary;
                        background = Alpha(InkWashTheme.GoldPrimary, 0.06f);
                        break;

                    case MilestoneState.Claimed:
                        border = InkWashTheme.BorderNeutralL2;
                        background = Alpha(InkWashTheme.Abyss, 0.5f);
                        break;

                    default:
                        border = InkWashTheme.BorderNeutralL1;
                        background = Alpha(InkWashTheme.Abyss, 0.5f);
                        break;
                }

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, background);
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, border, 1f);
                base.Draw();
            }
        }
    }
}
