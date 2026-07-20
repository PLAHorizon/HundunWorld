using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Shop
{
    public class ShopRareItemsPage : ContainerControl, IInkPage
    {
        private Float2 _screenSize;

        private InkButton _backButton;
        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkPanel _contentArea;

        private InkPanel[] _tabButtons;
        private InkPanel[] _filterButtons;

        private InkPanel[] _itemCards;

        private InkPanel _modalOverlay;
        private InkPanel _modalPanel;

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
        private string[] _itemCountdowns = { "12:30:45", "", "", "", "", "", "", "", "" };

        private Color[] _qualityColors = {
            InkWashTheme.QualityCommon,
            InkWashTheme.QualityUncommon,
            InkWashTheme.QualityRare,
            InkWashTheme.QualityEpic,
            InkWashTheme.QualityLegendary
        };

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
                Parent = _leftNav
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
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _leftNav
            };

            var logoSub = new Label
            {
                Text = "HUNDUN WORLD",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
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
            }

            var footerDivider = new InkDivider
            {
                Width = 208f,
                Height = 1f,
                Parent = _leftNav
            };

            var versionLabel = new Label
            {
                Text = "v1.2.0 · 江湖公测",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
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
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = this
            };

            var avatarCircle = new ContainerControl
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
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = avatarCircle
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
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var currencyCoin = new Label
            {
                Text = "12,450",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            var currencyCoinLabel = new Label
            {
                Text = "铜钱",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _topBar
            };

            var currencyIngot = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            var currencyIngotLabel = new Label
            {
                Text = "元宝",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextBrand,
                Parent = _topBar
            };

            var timeLabel = new Label
            {
                Text = "戌时 三刻",
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
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = _tabButtons[i]
                };
            }

            var refreshLabel = new Label
            {
                Text = "刷新时间",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _contentArea
            };

            var refreshTime = new Label
            {
                Text = "02:45:30",
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

            var itemCountLabel = new Label
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
                _itemCards[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _contentArea
                };

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
            var hintLabel = new Label
            {
                Text = "限时珍品倒计时结束后将下架，江湖过客请把握时机。购买后商品将送至您的信箱。",
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

            _modalPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.StretchAll,
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

            var closeButton = new InkButton
            {
                Width = 32f,
                Height = 32f,
                Text = "×",
                Variant = InkButtonVariant.Ghost,
                Parent = _modalPanel
            };

            var modalIconPanel = new InkPanel
            {
                Size = new Float2(80f, 80f),
                BackgroundColor = InkWashTheme.GoldPrimary * 0.12f,
                Parent = _modalPanel
            };

            var modalIconLabel = new Label
            {
                Text = "青",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = modalIconPanel
            };

            var modalTitle = new Label
            {
                Text = "青锋剑·绝世",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _modalPanel
            };

            var qualityBadge = new ContainerControl
            {
                Width = 60f,
                Height = 22f,
                BackgroundColor = InkWashTheme.VermilionPrimary * 0.12f,
                Parent = _modalPanel
            };

            var qualityLabel = new Label
            {
                Text = "传世品质",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = qualityBadge
            };

            var descTitle = new Label
            {
                Text = "物品描述",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _modalPanel
            };

            var descText = new Label
            {
                Text = "上古名剑，锋利无比。剑身通体如墨，挥之有龙吟之声，相传为上古铸剑大师以天外陨铁淬炼七七四十九日而成。",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextSecondary,
                Parent = _modalPanel
            };

            var attrTitle = new Label
            {
                Text = "属性详情",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _modalPanel
            };

            var skillTitle = new Label
            {
                Text = "特殊效果",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _modalPanel
            };

            var skillText = new Label
            {
                Text = "剑气纵横：攻击时有15%概率释放剑气，造成额外200%伤害",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _modalPanel
            };

            var priceLabel = new Label
            {
                Text = "购买价格",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _modalPanel
            };

            var priceValue = new Label
            {
                Text = "6480 元宝",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _modalPanel
            };

            var cancelButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "取消",
                Variant = InkButtonVariant.Ghost,
                Parent = _modalPanel
            };

            var buyButton = new InkButton
            {
                Width = 100f,
                Height = 36f,
                Text = "购买",
                Variant = InkButtonVariant.Primary,
                Parent = _modalPanel
            };
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