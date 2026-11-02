using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Shop
{
    public class ShopRareItemsPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private Float2 _screenSize;

        private InkButton _backButton;
        private InkPanel _leftNav;
        private InkPanel _topBar;
        private ContainerControl _headerAvatar;
        private Label _headerCharName;
        private Label _headerCharLevel;
        private Label _headerCoinCaption;
        private Label _headerCoin;
        private Label _headerIngotCaption;
        private Label _headerIngot;
        private Label _headerTime;
        private InkPanel _contentArea;

        private InkPanel[] _tabButtons;
        private InkPanel[] _filterButtons;

        private InkPanel[] _itemCards;

        // 页头/工具条上的散标签：索引式排布会随子控件数量漂移，改为按引用定位
        private Label _refreshHintLabel;
        private Label _refreshTimeLabel;
        private Label _itemCountLabel;
        private Label _bottomHintLabel;

        private InkPanel _modalOverlay;
        private InkPanel _modalPanel;
        private InkButton _modalCloseButton;
        private InkPanel _modalIconPanel;
        private Label _modalIconLabel;
        private Label _modalTitle;
        private ContainerControl _modalQualityBadge;
        private Label _modalQualityLabel;
        private Label _modalDescText;
        private Label _modalPriceValue;
        private InkButton _modalCancelButton;
        private InkButton _modalBuyButton;

        private string[] _itemNames = { "青锋剑·绝世", "紫霞秘籍", "龙纹玉佩", "玄铁重甲", "凌波残卷", "天山雪莲", "金丝软甲", "还魂丹", "百年人参" };
        private string[] _itemDescs = {
            "上古名剑，锋利无比",
            "紫霞派不传之秘",
            "龙纹环绕，护身辟邪",
            "玄铁锻造，坚不可摧",
            "凌波微步残卷",
            "天山特产，疗伤圣药",
            "金丝编织，轻便护体",
            "起死回生，满血复活",
            "百年药龄，大补之物"
        };
        private int[] _itemPrices = { 6480, 3280, 2680, 1280, 1880, 880, 1080, 480, 5000 };
        private string[] _itemCurrencies = { "元宝", "元宝", "元宝", "元宝", "元宝", "元宝", "元宝", "元宝", "铜钱" };
        private string[] _itemQualities = { "legendary", "epic", "epic", "rare", "epic", "rare", "rare", "uncommon", "uncommon" };
        private string[] _itemBadges = { "限时", "热销", "史品", "珍品", "仅剩3件", "珍品", "珍品", "良品", "良品" };
        private string[] _itemIllustChars = { "青", "紫", "龙", "玄", "凌", "莲", "金", "魂", "参" };
        // 限时倒计时没有服务端计时来源：整列留空，避免摆一个永远不动的假时间
        private string[] _itemCountdowns = { "", "", "", "", "", "", "", "", "" };

        private Color[] _qualityColors = {
            InkWashTheme.QualityCommon,
            InkWashTheme.QualityUncommon,
            InkWashTheme.QualityRare,
            InkWashTheme.QualityEpic,
            InkWashTheme.QualityLegendary
        };

        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

        public ShopRareItemsPage()
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
                BuildPageHeader();
                BuildTabBar();
                BuildFilterBar();
                BuildItemGrid();
                BuildBottomHint();
                BuildModal();

                ApplyLayout();
                RefreshAllData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[ShopRareItemsPage] 初始化失败: {ex.Message}");
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

            var navBack = new InkButton
            {
                Width = 232f,
                Height = 40f,
                Text = "返回江湖",
                Variant = InkButtonVariant.Ghost,
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                Location = new Float2(4f, 12f),
                Parent = _leftNav
            };
            // 返回战斗 HUD（设计隐藏出口 back-hud）
            navBack.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            var logoMark = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                Location = new Float2(16f, 60f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _leftNav
            };

            var logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                Size = new Float2(164f, 26f),
                Location = new Float2(60f, 58f),
                Parent = _leftNav
            };

            var logoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.TextDisabled,
                Size = new Float2(164f, 14f),
                Location = new Float2(60f, 84f),
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Location = new Float2(16f, 106f),
                Parent = _leftNav
            };

            string[] navItems = { "不肝", "抽卡", "活动", "通行证", "属性", "奇珍阁", "设置" };

            // 左侧导航即设计链路（批次5 §3.2）：不肝→抽卡→活动→通行证→属性→奇珍阁(自身)→设置
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

            float navY = 118f;
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

                if (i == 5)
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.BackgroundColor = InkWashTheme.GoldPrimary * 0.08f;
                    navItem.TextColor = InkWashTheme.GoldBright;
                }
                else
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.TextSecondary;
                }

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
                Location = new Float2(16f, navH - 58f),
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "v1.2.0 · 江湖公测",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Size = new Float2(208f, 16f),
                Location = new Float2(16f, navH - 44f),
                Parent = _leftNav
            };
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            // 顶栏九个控件原先都是局部变量、没有 Location/Size：全部叠在 (0,0) 且尺寸为 0，
            // 画面上只看得到页面外壳自己的昵称。改成字段，位置在 ApplyLayout 里按顶栏宽度排。
            _headerAvatar = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = InkWashTheme.BaseElevated,
                Parent = _topBar
            };

            var avatarLabel = new Label
            {
                Text = "客",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                // 头像圈固定 36×36：StretchAll 会被撑成 136×56，字被裁
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Vector2(36f, 36f),
                Parent = _headerAvatar
            };

            _headerCharName = new Label
            {
                // 初值留空位，真实昵称由下方 InkPlayerHeaderBinder 覆盖
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(120f, 60f),
                Parent = _topBar
            };

            _headerCharLevel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, 60f),
                Parent = _topBar
            };

            _headerCoinCaption = new Label
            {
                Text = "铜钱",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(40f, 60f),
                Parent = _topBar
            };

            _headerCoin = new Label
            {
                // 铜钱只在 ShopClientService 拿到服务端快照后才显示（binder 判断 Revision）
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(90f, 60f),
                Parent = _topBar
            };

            _headerIngotCaption = new Label
            {
                Text = "元宝",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(40f, 60f),
                Parent = _topBar
            };

            _headerIngot = new Label
            {
                // 元宝没有服务端快照（ShopClientService 只给 Gold）
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(70f, 60f),
                Parent = _topBar
            };

            _headerTime = new Label
            {
                // 世界时间同步没有接入客户端（同 MenuTimePage）
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(80f, 60f),
                Parent = _topBar
            };

            // 昵称/等级/铜钱有真实值时才覆盖；元宝与时辰没有数据源，已改为「—」
            InkPlayerHeaderBinder.Bind(_headerCharName, _headerCharLevel, _headerCoin);
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

        private void BuildPageHeader()
        {
            var headerTitle = new Label
            {
                Text = "奇珍阁",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _contentArea
            };

            var headerSubtitle = new Label
            {
                Text = "江湖异宝 · 限时珍品",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };

            var sealLabel = new Label
            {
                Text = "珍",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 36f),
                TextColor = InkWashTheme.VermilionPrimary * 0.6f,
                Parent = _contentArea
            };
        }

        private void BuildTabBar()
        {
            string[] tabNames = { "限时珍品", "常驻奇珍", "兑换商店" };
            _tabButtons = new InkPanel[tabNames.Length];

            for (int i = 0; i < tabNames.Length; i++)
            {
                _tabButtons[i] = new InkPanel
                {
                    Width = 120f,
                    Height = 44f,
                    BackgroundColor = i == 0 ? InkWashTheme.GoldPrimary * 0.1f : InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var tabLabel = new Label
                {
                    Text = tabNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    // 页签固定 120×44：StretchAll 会被撑成 220×64，文字偏出去
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Vector2(120f, 44f),
                    Parent = _tabButtons[i]
                };
            }

            _refreshHintLabel = new Label
            {
                Text = "刷新时间",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };

            _refreshTimeLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _contentArea
            };
        }

        private void BuildFilterBar()
        {
            string[] filterNames = { "全部", "传世", "史品", "珍品", "良品" };
            _filterButtons = new InkPanel[filterNames.Length];

            for (int i = 0; i < filterNames.Length; i++)
            {
                _filterButtons[i] = new InkPanel
                {
                    Height = 28f,
                    BackgroundColor = i == 0 ? InkWashTheme.GoldPrimary * 0.12f : InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

                var filterLabel = new Label
                {
                    Text = filterNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = _filterButtons[i]
                };
            }

            _itemCountLabel = new Label
            {
                Text = "共 9 件珍品",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };
        }

        private void BuildItemGrid()
        {
            _itemCards = new InkPanel[_itemNames.Length];

            for (int i = 0; i < _itemNames.Length; i++)
            {
                int cardIndex = i;
                var card = new ClickableCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };
                // 点卡片打开详情弹窗（弹窗本体此前从未接线，一直是死 UI）
                card.Clicked += () => OpenDetail(cardIndex);
                _itemCards[i] = card;

                var cornerDecoTL = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Parent = _itemCards[i]
                };

                var cornerDecoBR = new InkCornerDeco
                {
                    AnchorPreset = AnchorPresets.BottomRight,
                    Parent = _itemCards[i]
                };

                if (!string.IsNullOrEmpty(_itemBadges[i]))
                {
                    var badgePanel = new ContainerControl
                    {
                        Width = 48f,
                        Height = 20f,
                        BackgroundColor = _itemBadges[i].Contains("限时") || _itemBadges[i].Contains("仅剩")
                            ? InkWashTheme.VermilionPrimary * 0.12f
                            : InkWashTheme.GoldPrimary * 0.12f,
                        Parent = _itemCards[i]
                    };

                    var badgeLabel = new Label
                    {
                        Text = _itemBadges[i],
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                        TextColor = _itemBadges[i].Contains("限时") || _itemBadges[i].Contains("仅剩")
                            ? InkWashTheme.VermilionBright
                            : InkWashTheme.GoldBright,
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.StretchAll,
                        Parent = badgePanel
                    };
                }

                var iconPanel = new InkPanel
                {
                    Size = new Float2(72f, 72f),
                    BackgroundColor = GetQualityBgColor(_itemQualities[i]),
                    Parent = _itemCards[i]
                };

                var iconLabel = new Label
                {
                    Text = _itemIllustChars[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                    TextColor = GetQualityTextColor(_itemQualities[i]),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = iconPanel
                };

                var itemName = new Label
                {
                    Text = _itemNames[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = GetQualityTextColor(_itemQualities[i]),
                    Parent = _itemCards[i]
                };

                var itemDesc = new Label
                {
                    Text = _itemDescs[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    Parent = _itemCards[i]
                };

                var priceLabel = new Label
                {
                    Text = $"{_itemPrices[i]} {_itemCurrencies[i]}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = _itemCurrencies[i] == "铜钱" ? InkWashTheme.PaperAged : InkWashTheme.GoldBright,
                    Parent = _itemCards[i]
                };

                if (!string.IsNullOrEmpty(_itemCountdowns[i]))
                {
                    var countdownLabel = new Label
                    {
                        Text = _itemCountdowns[i],
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                        TextColor = InkWashTheme.VermilionBright,
                        Parent = _itemCards[i]
                    };
                }

                var bottomBorder = new ContainerControl
                {
                    Height = 2f,
                    BackgroundColor = GetQualityTextColor(_itemQualities[i]),
                    AnchorPreset = AnchorPresets.BottomLeft,
                    Parent = _itemCards[i]
                };
            }
        }

        private void BuildBottomHint()
        {
            _bottomHintLabel = new Label
            {
                Text = "珍品阁的货架与刷新计划尚未接入服务端，当前展示为目录样例。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                Parent = _contentArea
            };
        }

        private void BuildModal()
        {
            _modalOverlay = new InkPanel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(0, 0, 0, 0.72f),
                Visible = false,
                Parent = this
            };
            // 点遮罩空白处关闭（弹窗本体不冒泡到这里：OnMouseDown 返回 true）
            var overlayHit = new ModalOverlayHit
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _modalOverlay
            };
            overlayHit.Clicked += CloseDetail;

            _modalPanel = new InkPanel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                Visible = false,
                Parent = _modalOverlay
            };

            var cornerDecoTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _modalPanel
            };

            var cornerDecoTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _modalPanel
            };

            var cornerDecoBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _modalPanel
            };

            var cornerDecoBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _modalPanel
            };

            // 弹窗固定 500×600（见 ApplyLayout），内部子控件按设计稿顺序显式定位。
            // 原来这一整块 13 个控件都没有 Location：弹窗一旦显示就是一坨叠在左上角。
            _modalCloseButton = new InkButton
            {
                Width = 32f,
                Height = 32f,
                Text = "×",
                Variant = InkButtonVariant.Ghost,
                Location = new Float2(444f, 16f),
                Parent = _modalPanel
            };
            _modalCloseButton.Clicked += CloseDetail;

            _modalIconPanel = new InkPanel
            {
                Size = new Float2(80f, 80f),
                Location = new Float2(24f, 24f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _modalPanel
            };

            _modalIconLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _modalIconPanel
            };

            _modalTitle = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(120f, 28f),
                Size = new Float2(300f, 32f),
                Parent = _modalPanel
            };

            _modalQualityBadge = new ContainerControl
            {
                Width = 72f,
                Height = 22f,
                Location = new Float2(120f, 64f),
                BackgroundColor = InkWashTheme.VermilionPrimary * 0.12f,
                Parent = _modalPanel
            };

            _modalQualityLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _modalQualityBadge
            };

            var descTitle = new Label
            {
                Text = "物品描述",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 124f),
                Size = new Float2(452f, 20f),
                Parent = _modalPanel
            };

            _modalDescText = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords,
                Location = new Float2(24f, 150f),
                Size = new Float2(452f, 64f),
                Parent = _modalPanel
            };

            // 目录里没有属性/特效字段：如实说明，不摆设计稿的假词条
            var dataNote = new Label
            {
                Text = "属性与特殊效果数据尚未接入服务端",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDisabled,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 232f),
                Size = new Float2(452f, 18f),
                Parent = _modalPanel
            };

            var priceLabel = new Label
            {
                Text = "购买价格",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 470f),
                Size = new Float2(120f, 18f),
                Parent = _modalPanel
            };

            _modalPriceValue = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Location = new Float2(24f, 492f),
                Size = new Float2(220f, 28f),
                Parent = _modalPanel
            };

            _modalCancelButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "取消",
                Variant = InkButtonVariant.Ghost,
                Location = new Float2(256f, 540f),
                Parent = _modalPanel
            };
            _modalCancelButton.Clicked += CloseDetail;

            _modalBuyButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "购买",
                Variant = InkButtonVariant.Primary,
                Location = new Float2(372f, 540f),
                Parent = _modalPanel
            };
            _modalBuyButton.Clicked += OnModalBuyClicked;

            // 这两处的子标签用 StretchAll，但父容器尺寸是构造期给的，标签实测还停在 180×100 / 172×42，
            // 居中后落到父容器外面被裁掉（与珍品卡片同款陷阱，卡片走的是 LayoutCardChildren 里的 FitOnlyChild）。
            FitOnlyChild(_modalIconPanel);
            FitOnlyChild(_modalQualityBadge);
        }

        /// <summary>弹窗「购买」：珍品阁没有下单/扣款链路，只给明确提示。</summary>
        private void OnModalBuyClicked()
        {
            HundunWorld.Game.UI.UIHelper.ShowToast("珍品阁购买尚未接入服务端", HundunWorld.Game.UI.ToastType.Info);
        }

        /// <summary>打开某件珍品的详情弹窗：把目录数据填进弹窗，再显示遮罩。</summary>
        private void OpenDetail(int index)
        {
            if (_modalOverlay == null || _modalPanel == null) return;
            if (index < 0 || index >= _itemNames.Length) return;

            if (_modalIconLabel != null) _modalIconLabel.Text = _itemIllustChars[index];
            if (_modalTitle != null) _modalTitle.Text = _itemNames[index];
            if (_modalQualityLabel != null) _modalQualityLabel.Text = QualityNameOf(_itemQualities[index]);
            if (_modalQualityBadge != null)
                _modalQualityBadge.BackgroundColor = GetQualityBgColor(_itemQualities[index]) * 0.6f;
            if (_modalQualityLabel != null) _modalQualityLabel.TextColor = GetQualityTextColor(_itemQualities[index]);
            if (_modalDescText != null) _modalDescText.Text = _itemDescs[index];
            if (_modalPriceValue != null)
                _modalPriceValue.Text = $"{_itemPrices[index]} {_itemCurrencies[index]}";

            _modalOverlay.Visible = true;
            _modalPanel.Visible = true;
        }

        private void CloseDetail()
        {
            if (_modalPanel != null) _modalPanel.Visible = false;
            if (_modalOverlay != null) _modalOverlay.Visible = false;
        }

        private static string QualityNameOf(string quality)
        {
            switch (quality)
            {
                case "legendary": return "传世";
                case "epic": return "史诗";
                case "rare": return "稀有";
                case "uncommon": return "精良";
                default: return "普通";
            }
        }

        /// <summary>可点击的珍品卡片（InkPanel 本身没有 Clicked 事件）。</summary>
        private sealed class ClickableCard : InkPanel
        {
            public event System.Action Clicked;

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseDown(location, button);
            }
        }

        /// <summary>遮罩命中区：点空白关弹窗；弹窗本体盖在它上面并吞掉点击。</summary>
        private sealed class ModalOverlayHit : Control
        {
            public event System.Action Clicked;

            public ModalOverlayHit()
            {
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseDown(location, button);
            }
        }

        // 按设计方案 §4.1 品质色阶：Legendary=鎏金 #C8A858（非朱红）
        private Color GetQualityBgColor(string quality)
        {
            switch (quality)
            {
                case "legendary": return InkWashTheme.QualityLegendary * 0.15f;
                case "epic": return InkWashTheme.QualityEpic * 0.15f;
                case "rare": return InkWashTheme.QualityRare * 0.15f;
                case "uncommon": return InkWashTheme.QualityUncommon * 0.15f;
                default: return InkWashTheme.QualityCommon * 0.15f;
            }
        }

        private Color GetQualityTextColor(string quality)
        {
            switch (quality)
            {
                case "legendary": return InkWashTheme.QualityLegendary;
                case "epic": return InkWashTheme.QualityEpic;
                case "rare": return InkWashTheme.QualityRare;
                case "uncommon": return InkWashTheme.QualityUncommon;
                default: return InkWashTheme.QualityCommon;
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

                // 左簇：头像 + 昵称 + 等级；右簇：时辰 / 元宝 / 铜钱，按顶栏实际宽度靠右排
                float barRight = _topBar.Size.X - 16f;
                if (_headerAvatar != null) _headerAvatar.Location = new Float2(16f, 12f);
                if (_headerCharName != null) _headerCharName.Location = new Float2(62f, 0f);
                if (_headerCharLevel != null) _headerCharLevel.Location = new Float2(186f, 0f);

                if (_headerTime != null)
                {
                    _headerTime.Location = new Float2(barRight - _headerTime.Size.X, 0f);
                    barRight -= _headerTime.Size.X + 10f;
                }
                if (_headerIngotCaption != null)
                {
                    barRight -= _headerIngotCaption.Size.X;
                    _headerIngotCaption.Location = new Float2(barRight, 0f);
                }
                if (_headerIngot != null)
                {
                    barRight -= _headerIngot.Size.X + 4f;
                    _headerIngot.Location = new Float2(barRight, 0f);
                }
                barRight -= 14f;
                if (_headerCoinCaption != null)
                {
                    barRight -= _headerCoinCaption.Size.X;
                    _headerCoinCaption.Location = new Float2(barRight, 0f);
                }
                if (_headerCoin != null)
                {
                    barRight -= _headerCoin.Size.X + 4f;
                    _headerCoin.Location = new Float2(barRight, 0f);
                }
            }

            if (_contentArea != null)
            {
                _contentArea.Size = new Float2(sw - 240f, sh - 60f);
                _contentArea.Location = new Float2(240f, 60f);
            }

            float padding = 32f;
            float contentWidth = sw - 240f;

            var children = _contentArea?.Children.ToArray();
            int childIdx = 0;

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(padding, padding);
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(padding, padding + 40f);
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(contentWidth - padding - 50f, padding);
            }

            float tabX = padding;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].Location = new Float2(tabX, 120f);
                    tabX += 120f;
                }
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(contentWidth - padding - 60f, 130f);
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(contentWidth - padding - 130f, 130f);
            }

            float filterX = padding;
            float[] filterWidths = { 50f, 50f, 50f, 50f, 50f };
            for (int i = 0; i < _filterButtons.Length; i++)
            {
                if (_filterButtons[i] != null)
                {
                    _filterButtons[i].Size = new Float2(filterWidths[i], 28f);
                    _filterButtons[i].Location = new Float2(filterX, 180f);
                    filterX += filterWidths[i] + 8f;
                }
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(contentWidth - padding - 80f, 185f);
            }

            float cardWidth = (contentWidth - padding * 2f - 24f) / 3f;
            float cardHeight = 200f;
            float cardGridX = padding;
            float cardGridY = 220f;

            for (int i = 0; i < _itemCards.Length; i++)
            {
                if (_itemCards[i] != null)
                {
                    _itemCards[i].Size = new Float2(cardWidth, cardHeight);
                    _itemCards[i].Location = new Float2(cardGridX, cardGridY);
                    LayoutCardChildren(i, cardWidth, cardHeight);
                }

                cardGridX += cardWidth + 12f;
                if ((i + 1) % 3 == 0)
                {
                    cardGridX = padding;
                    cardGridY += cardHeight + 16f;
                }
            }

            if (children != null && childIdx < children.Length)
            {
                children[childIdx++].Location = new Float2(padding, cardGridY + 20f);
                children[childIdx - 1].Width = contentWidth - padding * 2f;
            }

            // 工具条/底部提示按引用定位（索引式在子控件增删后会漂到标题上）
            if (_refreshHintLabel != null)
            {
                _refreshHintLabel.Location = new Float2(contentWidth - padding - 210f, 126f);
                _refreshHintLabel.Size = new Float2(92f, 18f);
            }
            if (_refreshTimeLabel != null)
            {
                _refreshTimeLabel.Location = new Float2(contentWidth - padding - 112f, 124f);
                _refreshTimeLabel.Size = new Float2(100f, 20f);
                _refreshTimeLabel.HorizontalAlignment = TextAlignment.Far;
            }
            if (_itemCountLabel != null)
            {
                _itemCountLabel.Location = new Float2(contentWidth - padding - 110f, 185f);
                _itemCountLabel.Size = new Float2(100f, 18f);
                _itemCountLabel.HorizontalAlignment = TextAlignment.Far;
            }
            if (_bottomHintLabel != null)
            {
                _bottomHintLabel.Location = new Float2(padding, cardGridY + 20f);
                _bottomHintLabel.Size = new Float2(contentWidth - padding * 2f, 18f);
            }
            for (int i = 0; i < _filterButtons.Length; i++)
                FitOnlyChild(_filterButtons[i]);

            if (_modalOverlay != null)
            {
                _modalOverlay.Size = new Float2(sw, sh);
                _modalOverlay.Location = Float2.Zero;
            }

            if (_modalPanel != null)
            {
                _modalPanel.Size = new Float2(500f, 600f);
                _modalPanel.Location = new Float2((sw - 500f) / 2f, (sh - 600f) / 2f);
            }
        }

        /// <summary>
        /// 珍品卡片内部排布：子控件构造时都没有位置，全靠这里按"是否带角标/倒计时"推算下标。
        /// 顺序：角落装饰 ×2 → [角标] → 图标 → 名称 → 描述 → 价格 → [倒计时] → 底部色条。
        /// </summary>
        private void LayoutCardChildren(int index, float width, float height)
        {
            var card = _itemCards[index];
            if (card == null || card.ChildrenCount == 0)
                return;

            var children = card.Children.ToArray();
            int i = 0;
            i += 2; // 两个角落装饰自己按锚点画

            if (!string.IsNullOrEmpty(_itemBadges[index]) && i < children.Length)
            {
                children[i].Location = new Float2(Mathf.Max(0f, width - 56f), 10f);
                children[i].Size = new Float2(48f, 20f);
                FitOnlyChild(children[i]);
                i++;
            }

            if (i < children.Length)
            {
                children[i].Location = new Float2(16f, 16f);
                children[i].Size = new Float2(72f, 72f);
                FitOnlyChild(children[i]);
                i++;
            }
            if (i < children.Length)
            {
                children[i].Location = new Float2(16f, 96f);
                children[i].Size = new Float2(Mathf.Max(60f, width - 32f), 22f);
                i++;
            }
            if (i < children.Length)
            {
                children[i].Location = new Float2(16f, 122f);
                children[i].Size = new Float2(Mathf.Max(60f, width - 32f), 34f);
                if (children[i] is Label desc)
                    desc.Wrapping = TextWrapping.WrapWords;
                i++;
            }
            if (i < children.Length)
            {
                children[i].Location = new Float2(16f, 164f);
                children[i].Size = new Float2(Mathf.Max(80f, width - 130f), 18f);
                i++;
            }
            if (!string.IsNullOrEmpty(_itemCountdowns[index]) && i < children.Length)
            {
                children[i].Location = new Float2(Mathf.Max(0f, width - 110f), 165f);
                children[i].Size = new Float2(100f, 16f);
                if (children[i] is Label cd)
                    cd.HorizontalAlignment = TextAlignment.Far;
                i++;
            }
        }

        /// <summary>
        /// 容器里的唯一子控件按父尺寸铺满：这些子标签用的是 StretchAll 锚点，
        /// 但父容器尺寸是布局期才改的，光设 Size 不一定触发重排，干脆显式给一次。
        /// </summary>
        private static void FitOnlyChild(Control parent)
        {
            if (parent is not ContainerControl container || container.ChildrenCount != 1)
                return;

            var only = container.Children[0];
            // 先把锚点从 StretchAll 摘下来：留着它会在下一次布局把刚设好的 Size 又撑回"父+100"
            only.AnchorPreset = AnchorPresets.TopLeft;
            only.Location = Float2.Zero;
            only.Size = container.Size;
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
        }

        /// <summary>自测标记：环境没有鼠标注入，用标记文件打开详情弹窗并留回执（同 friends-selftest 做法）。</summary>
        private bool _detailSelftestDone;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_detailSelftestDone || !VisibleInHierarchy) return;

            try
            {
                string marker = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", "rare-detail.on");
                if (!System.IO.File.Exists(marker)) return;

                _detailSelftestDone = true;
                OpenDetail(0);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "HundunWorld", "ui-selftest.out"),
                    $"rare-detail open=1 overlay={_modalOverlay?.Visible} panel={_modalPanel?.Visible} " +
                    $"title={_modalTitle?.Text.ToString()} price={_modalPriceValue?.Text.ToString()} desc={_modalDescText?.Text.ToString()}"
                    + System.Environment.NewLine);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[ShopRareItemsPage] 详情自测失败: {ex.Message}");
            }
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