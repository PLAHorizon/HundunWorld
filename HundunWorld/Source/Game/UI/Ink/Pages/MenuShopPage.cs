using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 商城页面 —— 对应 game-ui-system/pages/shop.html
    /// </summary>
    public class MenuShopPage : ContainerControl, IInkPage
    {
        // ───────────── Data Types ─────────────

        private class ShopProduct
        {
            public string Name;
            public InkWashTheme.InkQuality Quality;
            public int Price;
            public string Currency; // 金/钻/荣誉
            public int Limit; // 0 = no limit
            public string IconText;
        }

        private class CartItem
        {
            public ShopProduct Product;
            public int Qty;
        }

        // ───────────── Fields ─────────────

        private Float2 _screenSize;

        // Header
        private Panel _header;
        private Label _title;
        private List<InkButton> _tabs;
        private InkButton _closeBtn;

        // Body
        private Panel _body;
        private Panel _gridPanel;
        private List<ClickableCard> _cards;
        private Panel _cartPanel;
        private Label _cartTitle;
        private InkButton _cartClear;
        private Panel _cartItems;
        private Label _cartTotal;
        private Panel _balances;
        private InkButton _buyBtn;
        private Label _footerInfo;

        private int _selectedTab;
        private ShopProduct[] _products;
        private List<CartItem> _cart;

        private const float HeaderH = 60f;
        private const float CartW = 360f;
        private const float FooterH = 40f;

        public event Action<string> NavigationRequested;

        // ───────────── Clickable Card ─────────────

        private class ClickableCard : Panel
        {
            public event Action Clicked;

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        // ───────────── Constructor ─────────────

        public MenuShopPage()
        {
            _screenSize = new Float2(Width, Height);
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
                _screenSize = new Float2(1920f, 1080f);

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            InitData();
            try
            {
                BuildBg();
                BuildHeader();
                BuildBody();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuShopPage] init: {ex.Message}");
            }
        }

        private void InitData()
        {
            _products = new ShopProduct[]
            {
                new ShopProduct { Name = "\u7384\u94C1\u5251", Quality = InkWashTheme.InkQuality.Epic, Price = 500, Currency = "\u91D1", Limit = 3, IconText = "\u5251" },
                new ShopProduct { Name = "\u5BD2\u94C1\u62A4\u8155", Quality = InkWashTheme.InkQuality.Rare, Price = 300, Currency = "\u91D1", IconText = "\u76FE" },
                new ShopProduct { Name = "\u751F\u547D\u836F\u6C34", Quality = InkWashTheme.InkQuality.Common, Price = 50, Currency = "\u91D1", IconText = "\u74F6" },
                new ShopProduct { Name = "\u7ECF\u9A8C\u4E39", Quality = InkWashTheme.InkQuality.Uncommon, Price = 50, Currency = "\u94BB", Limit = 5, IconText = "\u4E39" },
                new ShopProduct { Name = "\u94C1\u77FF", Quality = InkWashTheme.InkQuality.Common, Price = 10, Currency = "\u91D1", IconText = "\u77FF" },
                new ShopProduct { Name = "\u5750\u9A91\u4EE4", Quality = InkWashTheme.InkQuality.Legendary, Price = 100, Currency = "\u8363\u8A89", Limit = 1, IconText = "\u9A91" },
                new ShopProduct { Name = "\u5916\u89C2\u5305", Quality = InkWashTheme.InkQuality.Epic, Price = 200, Currency = "\u94BB", IconText = "\u8863" },
                new ShopProduct { Name = "\u5F3A\u5316\u77F3", Quality = InkWashTheme.InkQuality.Uncommon, Price = 100, Currency = "\u91D1", IconText = "\u77F3" },
                new ShopProduct { Name = "\u7CBE\u94C1\u5251", Quality = InkWashTheme.InkQuality.Rare, Price = 500, Currency = "\u91D1", IconText = "\u5251" },
                new ShopProduct { Name = "\u7384\u94C1\u7532", Quality = InkWashTheme.InkQuality.Epic, Price = 800, Currency = "\u91D1", Limit = 2, IconText = "\u76D4" },
                new ShopProduct { Name = "\u7075\u9B42\u77F3", Quality = InkWashTheme.InkQuality.Uncommon, Price = 30, Currency = "\u94BB", IconText = "\u7CBE" },
                new ShopProduct { Name = "\u4F20\u529F\u7B26", Quality = InkWashTheme.InkQuality.Rare, Price = 80, Currency = "\u8363\u8A89", Limit = 3, IconText = "\u7B26" },
            };

            _cart = new List<CartItem>();
            _cart.Add(new CartItem { Product = _products[8], Qty = 1 });
            _cart.Add(new CartItem { Product = _products[2], Qty = 5 });
        }

        private Color QualityColor(InkWashTheme.InkQuality q)
        {
            switch (q)
            {
                case InkWashTheme.InkQuality.Legendary: return InkWashTheme.QualityLegendary;
                case InkWashTheme.InkQuality.Epic: return InkWashTheme.QualityEpic;
                case InkWashTheme.InkQuality.Rare: return InkWashTheme.QualityRare;
                case InkWashTheme.InkQuality.Uncommon: return InkWashTheme.QualityUncommon;
                default: return InkWashTheme.QualityCommon;
            }
        }

        // ───────────── Build ─────────────

        private void BuildBg()
        {
            new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };
        }

        private void BuildHeader()
        {
            _header = new Panel
            {
                BackgroundColor = InkWashTheme.PanelSolid,
                Parent = this
            };

            _title = new Label
            {
                Text = "\u5546\u57CE",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _header
            };

            _tabs = new List<InkButton>();
            string[] tabNames = { "\u63A8\u8350", "\u6B66\u5668", "\u62A4\u7532", "\u6D88\u8017\u54C1", "\u6750\u6599", "\u7279\u6B8A" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int ci = i;
                var t = new InkButton
                {
                    Text = tabNames[i],
                    ButtonSize = InkButtonSize.Sm,
                    Variant = InkButtonVariant.Ghost,
                    Parent = _header
                };
                t.Clicked += () => SelectShopTab(ci);
                _tabs.Add(t);
            }
            SelectShopTab(0);

            _closeBtn = new InkButton
            {
                Text = "\u2715",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _header
            };
            _closeBtn.Clicked += () => NavigationRequested?.Invoke("combat-hud");
        }

        private void SelectShopTab(int idx)
        {
            _selectedTab = idx;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].TextColor = i == idx ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            }
        }

        private void BuildBody()
        {
            _body = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            // Product grid
            _gridPanel = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _body
            };

            _cards = new List<ClickableCard>();
            foreach (var prod in _products)
            {
                var card = new ClickableCard
                {
                    BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.5f),
                    Parent = _gridPanel
                };

                var iconPanel = new Panel
                {
                    Size = new Float2(48, 48),
                    BackgroundColor = new Color(QualityColor(prod.Quality).R, QualityColor(prod.Quality).G, QualityColor(prod.Quality).B, 0.15f),
                    Parent = card
                };
                new Label
                {
                    Text = prod.IconText,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f),
                    TextColor = QualityColor(prod.Quality),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = iconPanel
                };

                new Label
                {
                    Text = prod.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = card
                };

                new Label
                {
                    Text = $"{prod.Price} {prod.Currency}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = prod.Currency == "\u91D1" ? InkWashTheme.TextSecondary : InkWashTheme.GoldBright,
                    Parent = card
                };

                if (prod.Limit > 0)
                {
                    new Label
                    {
                        Text = $"\u9650\u8D2D{prod.Limit}",
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                        TextColor = InkWashTheme.BloodBright,
                        BackgroundColor = new Color(InkWashTheme.BloodPrimary.R, InkWashTheme.BloodPrimary.G, InkWashTheme.BloodPrimary.B, 0.12f),
                        Parent = card
                    };
                }

                int ci = _cards.Count;
                card.Clicked += () => AddToCart(ci);
                _cards.Add(card);
            }

            // Cart panel
            _cartPanel = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _body
            };

            _cartTitle = new Label
            {
                Text = "\u5DF2\u9009\u5546\u54C1",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.TextDefault,
                Parent = _cartPanel
            };

            _cartClear = new InkButton
            {
                Text = "\u6E05\u7A7A",
                ButtonSize = InkButtonSize.Sm,
                Variant = InkButtonVariant.Ghost,
                Parent = _cartPanel
            };
            _cartClear.Clicked += () => { _cart.Clear(); RefreshCart(); };

            _cartItems = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                Parent = _cartPanel
            };

            _cartTotal = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 16f),
                TextColor = InkWashTheme.GoldPrimary,
                Parent = _cartPanel
            };

            // Balances
            _balances = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _cartPanel
            };
            string[][] bals = {
                new[] { "\u91D1\u5E01", "12,450" },
                new[] { "\u94BB\u77F3", "85" },
                new[] { "\u8363\u8A89", "300" },
            };
            foreach (var b in bals)
            {
                var row = new ContainerControl
                {
                    BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.04f),
                    Parent = _balances
                };
                new Label
                {
                    Text = b[0],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = row
                };
                new Label
                {
                    Text = b[1],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Far,
                    Parent = row
                };
            }

            _buyBtn = new InkButton
            {
                Text = "\u786E\u8BA4\u8D2D\u4E70",
                ButtonSize = InkButtonSize.Lg,
                Variant = InkButtonVariant.Primary,
                Parent = _cartPanel
            };
            _buyBtn.Clicked += () => Debug.Log("[Shop] purchase confirmed");

            // Footer
            _footerInfo = new Label
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = _body
            };
            RefreshCart();
        }

        private void AddToCart(int prodIdx)
        {
            var prod = _products[prodIdx];
            foreach (var ci in _cart)
            {
                if (ci.Product == prod)
                {
                    ci.Qty++;
                    RefreshCart();
                    return;
                }
            }
            _cart.Add(new CartItem { Product = prod, Qty = 1 });
            RefreshCart();
        }

        private void RefreshCart()
        {
            foreach (var child in _cartItems.Children)
                child.Dispose();

            int total = 0;
            float iy = 0;
            foreach (var ci in _cart)
            {
                var row = new ContainerControl
                {
                    BackgroundColor = Color.Transparent,
                    Parent = _cartItems
                };
                new Label
                {
                    Text = ci.Product.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    Parent = row
                };
                new Label
                {
                    Text = $"x{ci.Qty}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextSecondary,
                    Parent = row
                };
                int subtotal = ci.Product.Price * ci.Qty;
                total += subtotal;
                new Label
                {
                    Text = $"{subtotal} {ci.Product.Currency}",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Far,
                    Parent = row
                };
                row.Location = new Float2(0, iy);
                row.Size = new Float2(CartW - 24, 24);
                iy += 28;
            }

            _cartTotal.Text = $"\u5408\u8BA1 {total}";
            _footerInfo.Text = $"\u80CC\u5305\u5BB9\u91CF 45/100    \u4ECA\u65E5\u9650\u8D2D\u5269\u4F59 7 \u4EF6";
        }

        // ───────────── Layout ─────────────

        private void ApplyLayout()
        {
            float sw = Width > 0 ? Width : _screenSize.X;
            float sh = Height > 0 ? Height : _screenSize.Y;

            float panelW = Math.Min(sw - 40, 1400f);
            float panelX = (sw - panelW) / 2;
            float panelH = sh - 40;

            // Header
            _header.Location = new Float2(panelX, 20);
            _header.Size = new Float2(panelW, HeaderH);

            _title.Location = new Float2(24, (HeaderH - 28) / 2);

            float tx = 120;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Location = new Float2(tx, (HeaderH - 28) / 2);
                _tabs[i].Size = new Float2(60, 28);
                tx += 70;
            }

            _closeBtn.Location = new Float2(panelW - 40, (HeaderH - 28) / 2);

            // Body
            float bodyY = 20 + HeaderH;
            float bodyH = panelH - HeaderH - FooterH - 40;
            _body.Location = new Float2(panelX, bodyY);
            _body.Size = new Float2(panelW, bodyH);

            // Grid
            float gw = panelW - CartW - 24;
            _gridPanel.Location = Float2.Zero;
            _gridPanel.Size = new Float2(gw, bodyH);

            int cols = 4;
            float gap = 12f;
            float cardW = (gw - gap * (cols - 1) - 24) / cols;
            float cardH = 80f;

            for (int i = 0; i < _cards.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                _cards[i].Location = new Float2(12 + col * (cardW + gap), 12 + row * (cardH + gap));
                _cards[i].Size = new Float2(cardW, cardH);

                int childIdx = 0;
                foreach (var child in _cards[i].Children)
                {
                    if (child is Panel icon && icon.Size == new Float2(48, 48))
                    {
                        icon.Location = new Float2(8, (cardH - 48) / 2);
                    }
                    else if (child is Label l)
                    {
                        string txt = l.Text.ToString();
                        bool isLimit = txt.Contains("\u9650\u8D2D");
                        bool isPrice = char.IsDigit(txt.Length > 0 ? txt[0] : ' ') || txt.Contains("\u91D1") || txt.Contains("\u94BB") || txt.Contains("\u8363");
                        if (isLimit)
                        {
                            l.Location = new Float2(cardW - 50, cardH - 20);
                            l.Size = new Float2(44, 16);
                        }
                        else if (isPrice)
                        {
                            l.Location = new Float2(64, cardH - 22);
                            l.Size = new Float2(cardW - 70, 18);
                        }
                        else if (childIdx == 1) // name label (first non-icon label)
                        {
                            l.Location = new Float2(64, 14);
                            l.Size = new Float2(cardW - 70, 18);
                        }
                        childIdx++;
                    }
                }
            }

            // Cart
            _cartPanel.Location = new Float2(gw + 24, 0);
            _cartPanel.Size = new Float2(CartW, bodyH);

            _cartTitle.Location = new Float2(16, 16);
            _cartClear.Location = new Float2(CartW - 72, 14);
            _cartClear.Size = new Float2(56, 24);

            float ciy = 48;
            _cartItems.Location = new Float2(12, ciy);
            _cartItems.Size = new Float2(CartW - 24, bodyH - ciy - 220);

            // Cart item layout is done in RefreshCart

            float totalY = bodyH - 210;
            _cartTotal.Location = new Float2(16, totalY);

            _balances.Location = new Float2(12, totalY + 30);
            _balances.Size = new Float2(CartW - 24, 80);

            float balY = 0;
            foreach (var child in _balances.Children)
            {
                if (child is ContainerControl row)
                {
                    row.Location = new Float2(0, balY);
                    row.Size = new Float2(CartW - 24, 24);
                    foreach (var sub in row.Children)
                    {
                        if (sub is Label l)
                        {
                            if (l.HorizontalAlignment == TextAlignment.Far)
                            {
                                l.Location = new Float2(row.Width - 100, 0);
                                l.Size = new Float2(96, 24);
                            }
                            else
                            {
                                l.Location = new Float2(8, 0);
                                l.Size = new Float2(80, 24);
                            }
                        }
                    }
                    balY += 26;
                }
            }

            _buyBtn.Location = new Float2(16, totalY + 120);
            _buyBtn.Size = new Float2(CartW - 32, 44);

            // Footer
            _footerInfo.Location = new Float2(panelX, 20 + HeaderH + bodyH + 8);
            _footerInfo.Size = new Float2(panelW, FooterH);
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
