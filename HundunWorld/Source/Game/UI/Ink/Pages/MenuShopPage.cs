using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using HundunWorld.Game.UI;

namespace HundunWorld.Game.UI.Ink.Pages
{
    public class MenuShopPage : ContainerControl, IInkPage
    {
        private const float LeftNavWidth = 240f;
        private const float TopBarHeight = 60f;
        private const float ConfirmPanelWidth = 300f;

        private InkPanelSolid _leftNavPanel;
        private InkPanelSolid _topBarPanel;
        private InkPanel _contentPanel;
        private InkPaperPanel _confirmPanel;

        private InkTextBlock _navVerticalTitle;
        private InkTextBlock _topBarName;
        private InkTag _topBarLevel;
        private InkTextBlock _topBarSect;
        private InkTextBlock _topBarCopper;
        private InkTextBlock _topBarSilver;

        private InkTextBlock _pageTitle;
        private TabButton[] _tabButtons;
        private ShopCard[] _shopCards;

        private InkTextBlock _confirmTitle;
        private InkTag _confirmQualityTag;
        private InkTextBlock[] _confirmItems;
        private InkTextBlock _confirmPrice;
        private InkTextBlock _confirmQty;
        private InkTextBlock _confirmTotal;
        private InkTextBlock _confirmBalance;
        private InkButton _confirmButton;
        private InkButton _cancelButton;

        private int _selectedTab = 0;
        private int _selectedCard = 0;
        private int _buyQty = 1;

        private Float2 _screenSize;

        public MenuShopPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0.55f);
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLeftNavigation();
                BuildTopBar();
                BuildContent();
                BuildConfirmPanel();

                ApplyLayout();
                SelectCard(_selectedCard);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuShopPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildLeftNavigation()
        {
            _leftNavPanel = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftNavPanel);

            InkPanel brandPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(LeftNavWidth, 60f),
                BackgroundColor = Color.Transparent,
            };
            _leftNavPanel.AddChild(brandPanel);

            InkTextBlock brandText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "混沌世界",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 14f),
                Size = new Float2(LeftNavWidth - 24f, 32f),
                HorizontalAlignment = TextAlignment.Center,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            brandPanel.AddChild(brandText);

            string[] navItems = { "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "", "休闲模式", "", "角色", "装备", "", "设置" };
            bool[] navActive = { false, false, false, false, false, false, true, false, false, false, false, false, false, false };

            for (int i = 0; i < navItems.Length; i++)
            {
                if (string.IsNullOrEmpty(navItems[i]))
                    continue;

                var item = new InkListItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 60f + i * 32f),
                    Size = new Float2(LeftNavWidth, 32f),
                    Active = navActive[i],
                };

                var label = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = navItems[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(24f, 0f),
                    Size = new Float2(LeftNavWidth - 24f, 32f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = navActive[i] ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary,
                };
                item.AddChild(label);
                _leftNavPanel.AddChild(item);
            }
        }

        private void BuildTopBar()
        {
            _topBarPanel = new InkPanelSolid
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_topBarPanel);

            InkPanel avatarPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 24f, 15f),
                Size = new Float2(32f, 32f),
                BackgroundColor = UIStyleTokens.BloodDeep,
            };
            _topBarPanel.AddChild(avatarPanel);

            _topBarName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "无名侠",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 64f, 18f),
                Size = new Float2(100f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.PaperBright,
            };
            _topBarPanel.AddChild(_topBarName);

            _topBarLevel = new InkTag
            {
                TagVariant = InkTagVariant.Brand,
                Text = "Lv.42",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 170f, 20f),
                Size = new Float2(56f, 22f),
            };
            _topBarPanel.AddChild(_topBarLevel);

            _topBarSect = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "逍遥派",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftNavWidth + 234f, 22f),
                Size = new Float2(80f, 18f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _topBarPanel.AddChild(_topBarSect);

            _topBarCopper = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "12,450",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 220f, 20f),
                Size = new Float2(80f, 20f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperBright,
            };
            _topBarPanel.AddChild(_topBarCopper);

            InkTextBlock copperLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "铜钱",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 280f, 22f),
                Size = new Float2(50f, 16f),
                TextColor = InkWashTheme.PaperAged,
            };
            _topBarPanel.AddChild(copperLabel);

            _topBarSilver = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "328",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 100f, 20f),
                Size = new Float2(60f, 20f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
            };
            _topBarPanel.AddChild(_topBarSilver);

            InkTextBlock silverLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "银两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(_screenSize.X - 150f, 22f),
                Size = new Float2(40f, 16f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            _topBarPanel.AddChild(silverLabel);
        }

        private void BuildContent()
        {
            _contentPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_contentPanel);

            InkPanel titlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(1f, 60f),
                BackgroundColor = Color.Transparent,
            };
            _contentPanel.AddChild(titlePanel);

            _navVerticalTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "市集",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(40f, 60f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f),
                TextColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.85f),
            };
            titlePanel.AddChild(_navVerticalTitle);

            _pageTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "商店",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(50f, 8f),
                Size = new Float2(200f, 44f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.TextBrand,
            };
            titlePanel.AddChild(_pageTitle);

            InkPanel tabsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 400f, 12f),
                Size = new Float2(400f, 36f),
                BackgroundColor = InkWashTheme.BaseTertiary,
            };
            titlePanel.AddChild(tabsPanel);

            string[] tabs = { "推荐", "装备", "消耗品", "材料", "外观", "礼包" };
            _tabButtons = new TabButton[6];
            float tabWidth = 400f / 6f;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = new TabButton(tabs[i], i)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(i * tabWidth, 0f),
                    Size = new Float2(tabWidth, 36f),
                    Active = (i == 0),
                };
                tab.Clicked += OnTabClicked;
                _tabButtons[i] = tab;
                tabsPanel.AddChild(tab);
            }

            var items = new[]
            {
                (name: "新手大礼包", quality: InkWashTheme.InkQuality.Legendary, price: 328, currency: "银两", tag: "传世"),
                (name: "神秘宝箱", quality: InkWashTheme.InkQuality.Epic, price: 200, currency: "银两", tag: "史"),
                (name: "精良装备箱", quality: InkWashTheme.InkQuality.Rare, price: 80, currency: "银两", tag: "珍"),
                (name: "精良武器", quality: InkWashTheme.InkQuality.Uncommon, price: 50, currency: "银两", tag: "良"),
                (name: "经验丹×10", quality: InkWashTheme.InkQuality.Uncommon, price: 20, currency: "银两", tag: "良"),
                (name: "染色剂套装", quality: InkWashTheme.InkQuality.Rare, price: 120, currency: "银两", tag: "珍"),
                (name: "修复锤×5", quality: InkWashTheme.InkQuality.Common, price: 500, currency: "铜钱", tag: "凡"),
                (name: "回城符×3", quality: InkWashTheme.InkQuality.Common, price: 300, currency: "铜钱", tag: "凡"),
                (name: "体力丹×5", quality: InkWashTheme.InkQuality.Common, price: 200, currency: "铜钱", tag: "凡"),
            };

            _shopCards = new ShopCard[9];
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var card = new ShopCard(item.name, item.quality, item.price, item.currency, item.tag, i)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 80f + i * 100f),
                    Size = new Float2(1f, 96f),
                    Selected = (i == _selectedCard),
                };
                card.Clicked += OnCardClicked;
                _shopCards[i] = card;
                _contentPanel.AddChild(card);
            }
        }

        private void BuildConfirmPanel()
        {
            _confirmPanel = new InkPaperPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_confirmPanel);

            InkCornerDeco corners = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(ConfirmPanelWidth, 500f),
            };
            _confirmPanel.AddChild(corners);

            InkPanel contentPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, 20f),
                Size = new Float2(ConfirmPanelWidth - 40f, 460f),
            };
            _confirmPanel.AddChild(contentPanel);

            InkPanel titleRow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(1f, 32f),
            };
            contentPanel.AddChild(titleRow);

            _confirmTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "新手大礼包",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(1f - 60f, 32f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 17f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            titleRow.AddChild(_confirmTitle);

            _confirmQualityTag = new InkTag
            {
                TagVariant = InkTagVariant.Default,
                Text = "传世",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 56f, 4f),
                Size = new Float2(52f, 24f),
            };
            titleRow.AddChild(_confirmQualityTag);

            InkTextBlock itemsLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "礼包内容",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 40f),
                Size = new Float2(1f, 18f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            contentPanel.AddChild(itemsLabel);

            string[] itemNames = { "经验丹", "修复锤", "回城符", "体力丹", "银两" };
            string[] itemCounts = { "×10", "×5", "×3", "×5", "×500" };
            _confirmItems = new InkTextBlock[10];
            for (int i = 0; i < itemNames.Length; i++)
            {
                InkPanel itemRow = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 64f + i * 24f),
                    Size = new Float2(1f, 22f),
                };
                contentPanel.AddChild(itemRow);

                _confirmItems[i * 2] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = itemNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(1f - 60f, 22f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.TextOnPaper,
                };
                itemRow.AddChild(_confirmItems[i * 2]);

                _confirmItems[i * 2 + 1] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = itemCounts[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 56f, 0f),
                    Size = new Float2(52f, 22f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.TextOnPaper,
                };
                itemRow.AddChild(_confirmItems[i * 2 + 1]);
            }

            InkDivider divider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 180f),
                Size = new Float2(1f, 1f),
            };
            contentPanel.AddChild(divider);

            InkPanel priceRow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 192f),
                Size = new Float2(1f, 24f),
            };
            contentPanel.AddChild(priceRow);

            InkTextBlock priceLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "单价",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(60f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            priceRow.AddChild(priceLabel);

            _confirmPrice = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "328 银两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 80f, 0f),
                Size = new Float2(76f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            priceRow.AddChild(_confirmPrice);

            InkPanel qtyRow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 224f),
                Size = new Float2(1f, 32f),
            };
            contentPanel.AddChild(qtyRow);

            InkTextBlock qtyLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "数量",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(60f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            qtyRow.AddChild(qtyLabel);

            InkButton qtyDecBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                Text = "-",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 100f, 4f),
                Size = new Float2(32f, 28f),
            };
            qtyDecBtn.ButtonClicked += (b) => { if (_buyQty > 1) { _buyQty--; UpdateConfirmPanel(); } };
            qtyRow.AddChild(qtyDecBtn);

            _confirmQty = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "1",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 64f, 4f),
                Size = new Float2(28f, 28f),
                HorizontalAlignment = TextAlignment.Center,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            qtyRow.AddChild(_confirmQty);

            InkButton qtyIncBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                Text = "+",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 32f, 4f),
                Size = new Float2(28f, 28f),
            };
            qtyIncBtn.ButtonClicked += (b) => { if (_buyQty < 10) { _buyQty++; UpdateConfirmPanel(); } };
            qtyRow.AddChild(qtyIncBtn);

            InkPanel totalPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 264f),
                Size = new Float2(1f, 40f),
                BackgroundColor = UIStyleTokens.WithAlpha(UIStyleTokens.BloodPrimary, 0.08f),
            };
            contentPanel.AddChild(totalPanel);

            InkTextBlock totalLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "总价",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(60f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            totalPanel.AddChild(totalLabel);

            _confirmTotal = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "328 银两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 80f, 6f),
                Size = new Float2(76f, 28f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.GoldPrimary,
            };
            totalPanel.AddChild(_confirmTotal);

            InkPanel balanceRow = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 312f),
                Size = new Float2(1f, 24f),
            };
            contentPanel.AddChild(balanceRow);

            InkTextBlock balanceLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "当前银两",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(80f, 24f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            balanceRow.AddChild(balanceLabel);

            _confirmBalance = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "328（刚好够）",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 100f, 0f),
                Size = new Float2(96f, 24f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.TextOnPaper,
            };
            balanceRow.AddChild(_confirmBalance);

            InkPanel buttonPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 344f),
                Size = new Float2(1f, 44f),
            };
            contentPanel.AddChild(buttonPanel);

            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                Text = "确认购买",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(1f - 72f, 44f),
            };
            buttonPanel.AddChild(_confirmButton);

            _cancelButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                Text = "取消",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(1f - 64f, 0f),
                Size = new Float2(60f, 44f),
            };
            buttonPanel.AddChild(_cancelButton);
        }

        private void OnTabClicked(int index)
        {
            _selectedTab = index;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].Active = (i == index);
            }
        }

        private void OnCardClicked(int index)
        {
            SelectCard(index);
        }

        private void SelectCard(int index)
        {
            _selectedCard = index;
            for (int i = 0; i < _shopCards.Length; i++)
            {
                _shopCards[i].Selected = (i == index);
            }

            var items = new[]
            {
                (name: "新手大礼包", quality: InkWashTheme.InkQuality.Legendary, price: 328, currency: "银两", tag: "传世"),
                (name: "神秘宝箱", quality: InkWashTheme.InkQuality.Epic, price: 200, currency: "银两", tag: "史"),
                (name: "精良装备箱", quality: InkWashTheme.InkQuality.Rare, price: 80, currency: "银两", tag: "珍"),
                (name: "精良武器", quality: InkWashTheme.InkQuality.Uncommon, price: 50, currency: "银两", tag: "良"),
                (name: "经验丹×10", quality: InkWashTheme.InkQuality.Uncommon, price: 20, currency: "银两", tag: "良"),
                (name: "染色剂套装", quality: InkWashTheme.InkQuality.Rare, price: 120, currency: "银两", tag: "珍"),
                (name: "修复锤×5", quality: InkWashTheme.InkQuality.Common, price: 500, currency: "铜钱", tag: "凡"),
                (name: "回城符×3", quality: InkWashTheme.InkQuality.Common, price: 300, currency: "铜钱", tag: "凡"),
                (name: "体力丹×5", quality: InkWashTheme.InkQuality.Common, price: 200, currency: "铜钱", tag: "凡"),
            };

            var item = items[index];
            _confirmTitle.Text = item.name;
            _confirmQualityTag.Text = item.tag;
            _confirmPrice.Text = $"{item.price} {item.currency}";
            _buyQty = 1;
            UpdateConfirmPanel();
        }

        private void UpdateConfirmPanel()
        {
            var items = new[]
            {
                (price: 328, currency: "银两"),
                (price: 200, currency: "银两"),
                (price: 80, currency: "银两"),
                (price: 50, currency: "银两"),
                (price: 20, currency: "银两"),
                (price: 120, currency: "银两"),
                (price: 500, currency: "铜钱"),
                (price: 300, currency: "铜钱"),
                (price: 200, currency: "铜钱"),
            };

            var item = items[_selectedCard];
            int total = item.price * _buyQty;
            _confirmQty.Text = _buyQty.ToString();
            _confirmTotal.Text = $"{total} {item.currency}";

            int balance = item.currency == "银两" ? 328 : 12450;
            string status = total <= balance ? "（刚好够）" : "（不足）";
            _confirmBalance.Text = $"{balance}{status}";
            if (total > balance)
            {
                _confirmBalance.TextColor = InkWashTheme.BloodBright;
            }
            else
            {
                _confirmBalance.TextColor = InkWashTheme.TextOnPaper;
            }
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_leftNavPanel != null)
            {
                _leftNavPanel.Location = new Float2(0f, 0f);
                _leftNavPanel.Size = new Float2(LeftNavWidth, sh);
            }

            if (_topBarPanel != null)
            {
                _topBarPanel.Location = new Float2(LeftNavWidth, 0f);
                _topBarPanel.Size = new Float2(sw - LeftNavWidth, TopBarHeight);
            }

            if (_topBarCopper != null)
            {
                _topBarCopper.Location = new Float2(sw - 220f, 20f);
            }
            if (_topBarSilver != null)
            {
                _topBarSilver.Location = new Float2(sw - 100f, 20f);
            }

            float contentWidth = sw - LeftNavWidth - ConfirmPanelWidth - 24f;
            if (_contentPanel != null)
            {
                _contentPanel.Location = new Float2(LeftNavWidth + 24f, TopBarHeight + 16f);
                _contentPanel.Size = new Float2(contentWidth, sh - TopBarHeight - 80f);
            }

            if (_shopCards != null)
            {
                int cols = sw >= 1400 ? 3 : sw >= 1000 ? 2 : 1;
                float cardWidth = (contentWidth - (cols - 1) * 16f) / cols;
                float cardHeight = 96f;

                for (int i = 0; i < _shopCards.Length; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    _shopCards[i].Location = new Float2(col * (cardWidth + 16f), 80f + row * (cardHeight + 16f));
                    _shopCards[i].Size = new Float2(cardWidth, cardHeight);
                }
            }

            if (_confirmPanel != null)
            {
                _confirmPanel.Location = new Float2(sw - ConfirmPanelWidth - 24f, TopBarHeight + 16f);
                _confirmPanel.Size = new Float2(ConfirmPanelWidth, 500f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        private class TabButton : ContainerControl
        {
            private string _text;
            private int _index;
            private bool _active;
            private InkTextBlock _label;

            public event Action<int> Clicked;

            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    if (_label != null)
                    {
                        _label.TextColor = _active ? InkWashTheme.TextOnBrand : InkWashTheme.TextSecondary;
                    }
                    if (_active)
                    {
                        BackgroundColor = new Color(InkWashTheme.GoldBright.R, InkWashTheme.GoldBright.G, InkWashTheme.GoldBright.B, 1f);
                    }
                    else
                    {
                        BackgroundColor = Color.Transparent;
                    }
                }
            }

            public TabButton(string text, int index)
            {
                _text = text;
                _index = index;
                ClipChildren = false;

                _label = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = text,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                    Size = new Float2(1f, 36f),
                    HorizontalAlignment = TextAlignment.Center,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.TextSecondary,
                };
                AddChild(_label);
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke(_index);
                }
                return true;
            }
        }

        private class ShopCard : ContainerControl
        {
            private string _name;
            private InkWashTheme.InkQuality _quality;
            private int _price;
            private string _currency;
            private string _tag;
            private int _index;
            private bool _selected;
            private InkPanel _qualityPanel;
            private InkTextBlock _nameLabel;
            private InkTag _qualityTag;
            private InkTextBlock _priceLabel;
            private InkButton _buyButton;
            private Color _borderColor = InkWashTheme.BorderGold;
            private float _borderThickness = 1f;

            public event Action<int> Clicked;

            public bool Selected
            {
                get => _selected;
                set
                {
                    _selected = value;
                    if (_selected)
                    {
                        BackgroundColor = new Color(InkWashTheme.VermilionPrimary.R, InkWashTheme.VermilionPrimary.G, InkWashTheme.VermilionPrimary.B, 0.08f);
                        _borderColor = InkWashTheme.VermilionPrimary;
                    }
                    else
                    {
                        BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f);
                        _borderColor = InkWashTheme.BorderGold;
                    }
                }
            }

            public ShopCard(string name, InkWashTheme.InkQuality quality, int price, string currency, string tag, int index)
            {
                _name = name;
                _quality = quality;
                _price = price;
                _currency = currency;
                _tag = tag;
                _index = index;
                ClipChildren = false;

                _qualityPanel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(1f, 80f),
                };
                AddChild(_qualityPanel);

                Color qualityColor = GetQualityColor(quality);
                _qualityPanel.BackgroundColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.12f);

                InkTextBlock iconText = new InkTextBlock(InkTextStyle.Display)
                {
                    Text = GetIconText(quality),
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 16f),
                    Size = new Float2(48f, 48f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                    TextColor = qualityColor,
                };
                _qualityPanel.AddChild(iconText);

                InkPanel bottomPanel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 80f),
                    Size = new Float2(1f, 16f),
                };
                AddChild(bottomPanel);

                _nameLabel = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = name,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 0f),
                    Size = new Float2(1f - 140f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.PaperBright,
                };
                bottomPanel.AddChild(_nameLabel);

                _qualityTag = new InkTag
                {
                    TagVariant = InkTagVariant.Default,
                    Text = tag,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 128f, 0f),
                    Size = new Float2(44f, 16f),
                };
                _qualityTag.TextColor = qualityColor;
                bottomPanel.AddChild(_qualityTag);

                _priceLabel = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = $"{price} {currency}",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(1f - 80f, 0f),
                    Size = new Float2(72f, 16f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = currency == "银两" ? InkWashTheme.GoldBright : InkWashTheme.PaperBright,
                };
                bottomPanel.AddChild(_priceLabel);
            }

            private static Color GetQualityColor(InkWashTheme.InkQuality quality)
            {
                // 按设计方案 §4.1 品质色阶：Legendary=#C8A858(鎏金)、Epic=#8B5E9E、Rare=#4A7EA8、Uncommon=#6B8E5A、Common=#8A8275
                switch (quality)
                {
                    case InkWashTheme.InkQuality.Legendary: return InkWashTheme.QualityLegendary;
                    case InkWashTheme.InkQuality.Epic: return InkWashTheme.QualityEpic;
                    case InkWashTheme.InkQuality.Rare: return InkWashTheme.QualityRare;
                    case InkWashTheme.InkQuality.Uncommon: return InkWashTheme.QualityUncommon;
                    default: return InkWashTheme.QualityCommon;
                }
            }

            private static string GetIconText(InkWashTheme.InkQuality quality)
            {
                switch (quality)
                {
                    case InkWashTheme.InkQuality.Legendary: return "礼";
                    case InkWashTheme.InkQuality.Epic: return "宝";
                    case InkWashTheme.InkQuality.Rare: return "箱";
                    case InkWashTheme.InkQuality.Uncommon: return "武";
                    default: return "物";
                }
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                {
                    Clicked?.Invoke(_index);
                }
                return true;
            }

            public override void Draw()
            {
                base.Draw();
                if (Width > 0f && Height > 0f && _borderThickness > 0f && _borderColor.A > 0f)
                {
                    Render2D.DrawRectangle(new Rectangle(0, 0, Width, Height), _borderColor, _borderThickness);
                }
            }
        }
    }
}