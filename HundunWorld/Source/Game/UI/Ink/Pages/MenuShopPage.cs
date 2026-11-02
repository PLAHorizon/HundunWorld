using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 商店（市集）页面。
    /// 唯一事实来源：设计稿 hundun-yy-ui/pages/menu-shop.html（燕云融合 · 深墨黑 + 鎏金 + 纸色卷轴）。
    /// 结构：
    ///   左侧 240px 导航栏（品牌「混沌世界」+ 任务/博物志/武林录/营生/组队/邮箱/商店
    ///     + 「休闲模式」快捷出口（快捷标签）+ 角色/装备 + 底部设置）
    ///   ＋ 顶部 60px 角色信息栏（返回战场 / 头像 / 无名侠 Lv.42 逍遥派 / 铜钱 12,450 / 银两 328）
    ///   ＋ 标题行（竖排「市集」+ 商店 + 推荐/装备/消耗品/材料/外观/礼包 分类标签）
    ///   ＋ 商品网格 9 张商品卡（品质色块图标 + 名称 + 品质标签 + 价格 + 购买）
    ///   ＋ 右侧 300px 纸色确认面板（礼包内容 / 单价 / 数量 / 总价 / 当前银两 / 确认购买 / 取消）
    ///   ＋ 页脚（混沌世界 · v1.2.0 ｜ 商店系统 · 仅供参考）
    /// 交互：导航跳转、分类筛选、选中商品、卡片购买、数量增减、确认购买、取消；
    ///       返回战场由顶部信息栏按钮触发（back-hud 出口）。
    /// </summary>
    public class MenuShopPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ───────────────────────── 数据模型 ─────────────────────────

        /// <summary>确认面板「礼包内容」中的一行（名称 + 数量）。</summary>
        private class ContentView
        {
            public string Name;
            public int Count;
        }

        /// <summary>商品数据（对应设计稿商品卡的名称 / 品质 / 价格 / 购买按钮 + 确认面板）。</summary>
        private class ShopProduct
        {
            /// <summary>商品名（设计稿卡片标题）。</summary>
            public string Name;

            /// <summary>品质文字（设计稿品质标签：传世 / 史 / 珍 / 良 / 凡）。</summary>
            public string QualityLabel;

            /// <summary>品质等级（决定品质色）。</summary>
            public InkWashTheme.InkQuality Quality;

            /// <summary>单价。</summary>
            public int Price;

            /// <summary>货币名（银两 / 铜钱）。</summary>
            public string Currency;

            /// <summary>图标字形（引擎无图标字体，沿用项目既有字形约定）。</summary>
            public string Icon;

            /// <summary>所属分类（对应分类标签；推荐页展示全部）。</summary>
            public string Category;

            /// <summary>确认面板「礼包内容」明细。</summary>
            public ContentView[] Contents;
        }

        // ───────────────────────── 布局常量 ─────────────────────────

        /// <summary>左侧导航栏宽度（设计稿 240px）。</summary>
        private const float SidebarWidth = 240f;

        /// <summary>导航栏品牌区高度（设计稿 60px）。</summary>
        private const float BrandHeight = 60f;

        /// <summary>导航项高度。</summary>
        private const float NavItemHeight = 40f;

        /// <summary>底部设置区高度。</summary>
        private const float SidebarFooterHeight = 56f;

        /// <summary>顶部角色信息栏高度（设计稿 60px）。</summary>
        private const float TopHeaderHeight = 60f;

        /// <summary>页脚高度（设计稿 py-3 + 11px 字）。</summary>
        private const float FooterHeight = 44f;

        /// <summary>内容区内边距（设计稿 p-6）。</summary>
        private const float ContentPadding = 24f;

        /// <summary>标题行高度（竖排「市集」+ 商店 32px）。</summary>
        private const float TitleRowHeight = 64f;

        /// <summary>右侧确认面板宽度（设计稿 300px）。</summary>
        private const float AsideWidth = 300f;

        /// <summary>商品网格与确认面板间距（设计稿 gap-5）。</summary>
        private const float GridAsideGap = 20f;

        /// <summary>商品卡间距（设计稿 gap-4）。</summary>
        private const float CardGap = 16f;

        /// <summary>商品卡高度（设计稿：16 内边距 + 80 图标块 + 名称行 + 价格行）。</summary>
        private const float CardHeight = 180f;

        /// <summary>分类标签栏高度。</summary>
        private const float TabBarHeight = 40f;

        /// <summary>分类标签宽度。</summary>
        private const float TabWidth = 72f;

        /// <summary>数量上限。</summary>
        private const int MaxQty = 99;

        /// <summary>设计稿分类标签：推荐 / 装备 / 消耗品 / 材料 / 外观 / 礼包。</summary>
        private static readonly string[] TabNames = { "推荐", "装备", "消耗品", "材料", "外观", "礼包" };

        // 左侧导航不再本页自维护：条目与分组统一由 InkSidebarNav 提供（见 Ink/InkSidebarNav.cs），
        // 由共用控件 InkSidebarList 渲染，因此"侧边栏能到达除弹框/事件 UI 之外的所有 UI"只需维护一处。

        // ───────────────────────── 字段 ─────────────────────────

        private Float2 _screenSize;

        // 氛围层（背景/暗角由外壳提供，页面不再持有）

        // 左侧导航栏
        private Panel _sidebar;
        private Panel _brandBlock;
        private Label _brandLabel;
        private InkDivider _brandDivider;
        private InkSidebarList _sidebarList;
        private Panel _sidebarFooter;
        private InkDivider _sidebarFooterDivider;
        private NavRow _settingsNav;

        // 顶部角色信息栏
        private Panel _mainArea;
        private Panel _topHeader;
        private InkButton _backButton;
        private CircleAvatar _playerAvatar;
        private Label _playerNameLabel;
        private InkTag _playerLevelTag;
        private Label _playerSectLabel;
        private Label _copperGlyphLabel;
        private Label _copperTitleLabel;
        private Label _copperValueLabel;
        private Label _silverGlyphLabel;
        private Label _silverTitleLabel;
        private Label _silverValueLabel;

        // 标题行 + 分类标签
        private Panel _contentArea;
        private InkVerticalTitle _verticalTitle;
        private Label _pageTitleLabel;
        private Panel _tabBar;
        private List<TabButton> _tabs;

        // 商品网格
        private Panel _gridScroll;
        private Panel _gridContent;
        private List<ProductCard> _cards;
        private Label _gridEmptyHint;

        // 右侧纸色确认面板
        private Panel _asidePanel;
        private InkPaperPanel _paperPanel;
        private InkCornerDeco _paperCorners;
        private Panel _detailScroll;
        private Panel _detailContent;
        private Label _detailTitleLabel;
        private QualityTag _detailQualityTag;
        private Label _contentsTitleLabel;
        private List<ContentViewRow> _contentRows;
        private InkDivider _detailDivider;
        private Label _unitPriceTitleLabel;
        private Label _unitPriceValueLabel;
        private Label _unitPriceCurrencyLabel;
        private Label _qtyTitleLabel;
        private PaperQtyButton _qtyMinusButton;
        private PaperQtyButton _qtyPlusButton;
        private Label _qtyValueLabel;
        private Panel _totalBox;
        private Label _totalTitleLabel;
        private Label _totalValueLabel;
        private Label _totalCurrencyLabel;
        private Label _balanceTitleLabel;
        private Label _balanceValueLabel;
        private Label _balanceHintLabel;
        private InkButton _confirmButton;
        private PaperOutlineButton _cancelButton;
        private Label _actionHintLabel;

        /// <summary>标题行的"示例货架"小字（没有商人上下文时显示，绑上商人后清空）。</summary>
        private Label _demoShelfLabel;

        // 页脚
        private Panel _footer;
        private InkDivider _footerDivider;
        private Label _footerLeftLabel;
        private Label _footerRightLabel;

        // 状态
        private ShopProduct[] _products;
        private int _selectedTab;
        private int _selectedIndex;
        private int _qty = 1;
        private int _silverBalance;
        private int _copperBalance;

        /// <summary>是否已收到服务端铜钱余额（false 时顶栏显示占位符，不拿示例数字充数）。</summary>
        private bool _hasCopperSnapshot;

        /// <summary>当前商人对应的运行时实体 id；0 = 没有商人上下文（页面仍是演示数据）。</summary>
        private ulong _vendorEntityId;

        /// <summary>卡片下标 → 服务端货架格子。只有服务端模式下才有意义。</summary>
        private readonly System.Collections.Generic.List<int> _serverSlotByCard = new System.Collections.Generic.List<int>();

        /// <summary>已渲染的商店数据版本（与服务端回执对账，见 Update）。</summary>
        private int _lastShopRevision = -1;

        /// <summary>页面导航请求（由 MainUIManager 通过 HookNavigationRequested 订阅）。</summary>
        public event Action<string> NavigationRequested;

        // ───────────────────────── 私有控件 ─────────────────────────

        /// <summary>
        /// 导航行 / 分类标签共用的行按钮基类：
        /// 自绘背景 + 激活指示线，避免依赖引擎 Button 的固定居中排版。
        /// </summary>
        private class NavRow : ContainerControl
        {
            /// <summary>激活指示线宽度（设计稿 2px）。</summary>
            private const float ActiveBarWidth = 2f;

            private readonly Label _label;
            private readonly InkTag _shortcutTag;
            private readonly bool _shortcut;
            private bool _active;
            private bool _muted;
            private bool _hovered;
            private bool _pressed;

            public event Action Clicked;

            /// <summary>是否可交互（次级导航中客户端暂无路由的项为 false）。</summary>
            public bool Interactable { get; set; } = true;

            /// <summary>是否处于激活态（当前页 / 当前分类）。</summary>
            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    ApplyColors();
                }
            }

            /// <summary>行文案。</summary>
            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            /// <summary>构造行按钮（shortcutTagText 非空时渲染右侧「快捷」标签）。</summary>
            public NavRow(string text, string shortcutTagText = null)
            {
                _shortcut = !string.IsNullOrEmpty(shortcutTagText);
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                if (_shortcut)
                {
                    _shortcutTag = new InkTag
                    {
                        Text = shortcutTagText,
                        TagVariant = InkTagVariant.Brand,
                        Parent = this
                    };
                }

                ApplyColors();
            }

            /// <summary>设置次级导航弱化配色（设计稿 shop-nav-item-muted）。</summary>
            public void SetMuted()
            {
                _muted = true;
                Interactable = false;
                ApplyColors();
            }

            private void ApplyColors()
            {
                if (_label == null)
                    return;

                if (_active || _shortcut)
                    _label.TextColor = InkWashTheme.TextBrand;
                else if (_muted)
                    _label.TextColor = InkWashTheme.TextTertiary;
                else
                    _label.TextColor = _hovered ? InkWashTheme.TextBrand : InkWashTheme.TextSecondary;
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_label == null)
                    return;

                const float tagWidth = 46f;
                float labelRight = _shortcut ? Width - tagWidth - 18f : Width - 12f;
                _label.Location = new Float2(24f, 0f);
                _label.Size = new Float2(Mathf.Max(0f, labelRight - 24f), Height);

                if (_shortcutTag != null)
                {
                    _shortcutTag.Location = new Float2(Width - tagWidth - 12f, (Height - 18f) * 0.5f);
                    _shortcutTag.Size = new Float2(tagWidth, 18f);
                }
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
                ApplyColors();
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
                _pressed = false;
                ApplyColors();
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && Interactable)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (Interactable && ContainsPoint(ref location))
                        Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                if (_active)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), WithAlpha(InkWashTheme.GoldPrimary, 0.1f));
                else if (_shortcut)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), WithAlpha(InkWashTheme.GoldPrimary, 0.08f));
                else if (_hovered && Interactable)
                    Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), WithAlpha(InkWashTheme.GoldPrimary, 0.06f));

                base.Draw();

                if (_shortcut)
                {
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), InkWashTheme.BorderGold, 1f);
                    Render2D.FillRectangle(new Rectangle(0f, 0f, ActiveBarWidth, Height), InkWashTheme.GoldBright);
                }
                else if (_active)
                {
                    Render2D.FillRectangle(new Rectangle(0f, 0f, ActiveBarWidth, Height), InkWashTheme.GoldPrimary);
                }
            }
        }

        /// <summary>
        /// 分类标签按钮（设计稿 .shop-tab / .shop-tab-active）：
        /// 激活态鎏金底 + 反色字 + 微金描边，未激活态透明底 + 次文字，悬停转品牌金。
        /// </summary>
        private class TabButton : ContainerControl
        {
            private readonly Label _label;
            private bool _active;
            private bool _hovered;
            private bool _pressed;

            public event Action Clicked;

            public bool Active
            {
                get => _active;
                set
                {
                    _active = value;
                    ApplyColors();
                }
            }

            public string Text
            {
                get => _label.Text;
                set => _label.Text = value;
            }

            public TabButton(string text)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _label = new Label
                {
                    Text = text,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                ApplyColors();
            }

            private void ApplyColors()
            {
                if (_label == null)
                    return;

                _label.TextColor = _active
                    ? InkWashTheme.TextOnBrand
                    : _hovered ? InkWashTheme.TextBrand : InkWashTheme.TextSecondary;
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                if (_label != null)
                {
                    _label.Location = Float2.Zero;
                    _label.Size = Size;
                }
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
                ApplyColors();
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
                _pressed = false;
                ApplyColors();
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                        Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);

                if (_active)
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, InkWashTheme.GoldPrimary);
                else if (_hovered)
                    InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, InkWashTheme.GoldTrace);

                base.Draw();

                if (_active)
                    InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, InkWashTheme.GoldDeep, 1f);
            }
        }

        /// <summary>
        /// 圆形玩家头像（设计稿顶部信息栏：朱红圆底 + 金环 + 字形）。
        /// </summary>
        private class CircleAvatar : ContainerControl
        {
            public CircleAvatar(string glyph)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                new Label
                {
                    Text = glyph,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = this
                };
            }


            /// <summary>
            /// 头像尺寸由调用方在构造后设置；子标签用 StretchAll 时不会跟着变（实测停在父尺寸+100），
            /// 这里在尺寸变化时把唯一子标签钉回父尺寸。
            /// </summary>
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (ChildrenCount > 0 && Children[0] is Control child)
                {
                    child.Location = Float2.Zero;
                    child.Size = Size;
                }
            }

            public override void Draw()
            {
                if (Visible && Width > 0f && Height > 0f)
                {
                    var center = new Float2(Width * 0.5f, Height * 0.5f);
                    float radius = Mathf.Min(Width, Height) * 0.5f - 1f;
                    InkRenderHelper.FillCircle(center, radius, InkWashTheme.VermilionPrimary);
                    InkRenderHelper.DrawCircle(center, radius, InkWashTheme.GoldDeep, 1f);
                }

                base.Draw();
            }
        }

        /// <summary>
        /// 品质标签（设计稿 .q-tag-common / uncommon / rare / epic / legendary）：
        /// 文字取品质色（传世取朱红亮色），底为品质色 12% 透明，描边为品质色 45% 透明。
        /// </summary>
        private class QualityTag : Label
        {
            private Color _borderColor = InkWashTheme.BorderNeutralL2;

            public QualityTag(InkWashTheme.InkQuality quality, string text)
            {
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f);
                HorizontalAlignment = TextAlignment.Center;
                VerticalAlignment = TextAlignment.Center;
                Text = text;
                ApplyQuality(quality);
            }

            /// <summary>按品质刷新标签配色。</summary>
            public void ApplyQuality(InkWashTheme.InkQuality quality)
            {
                Color qualityColor = QualityAccent(quality);
                float bgAlpha = quality == InkWashTheme.InkQuality.Legendary ? 0.14f : 0.12f;
                float borderAlpha = quality == InkWashTheme.InkQuality.Legendary ? 0.5f : 0.45f;

                BackgroundColor = WithAlpha(InkWashTheme.QualityColor(quality), bgAlpha);
                _borderColor = WithAlpha(InkWashTheme.QualityColor(quality), borderAlpha);
                TextColor = qualityColor;
            }

            public override void Draw()
            {
                base.Draw();

                if (_borderColor.A > 0f && Width > 0f && Height > 0f)
                    Render2D.DrawRectangle(new Rectangle(0f, 0f, Width, Height), _borderColor, 1f);
            }
        }

        /// <summary>
        /// 商品卡品质色块（设计稿 .q-tint-*）：品质色 14% 底 + 品质色 38% 描边 + 居中图标字形。
        /// </summary>
        private class QualityTint : ContainerControl
        {
            private readonly Color _tintColor;

            public QualityTint(Color tintColor, string glyph, Color glyphColor)
            {
                _tintColor = tintColor;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                new Label
                {
                    Text = glyph,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 30f),
                    TextColor = glyphColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Parent = this
                };
            }


            /// <summary>尺寸由调用方设置；子标签钉回父尺寸（StretchAll 不跟随，实测停父+100）。</summary>
            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                if (ChildrenCount > 0 && Children[0] is Control child)
                {
                    child.Location = Float2.Zero;
                    child.Size = Size;
                }
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusMd, WithAlpha(_tintColor, 0.14f));
                base.Draw();
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusMd, WithAlpha(_tintColor, 0.38f), 1f);
            }
        }

        /// <summary>
        /// 商品卡（设计稿 .shop-card / .shop-card-selected）：
        /// 品质色块 + 名称 + 品质标签 + 价格（货币字形 + 数值 + 货币名）+ 购买按钮。
        /// 选中态为朱红描边（传世强调），悬停为强金线。
        /// </summary>
        private class ProductCard : ContainerControl
        {
            private readonly QualityTint _tint;
            private readonly Label _nameLabel;
            private readonly QualityTag _qualityTag;
            private readonly Label _priceGlyphLabel;
            private readonly Label _priceValueLabel;
            private readonly Label _priceCurrencyLabel;
            private readonly InkButton _buyButton;

            private bool _selected;
            private bool _hovered;

            public event Action Clicked;
            public event Action BuyClicked;

            /// <summary>商品在页面数据表中的下标。</summary>
            public int ProductIndex { get; private set; }

            public ProductCard(int productIndex, ShopProduct product)
            {
                ProductIndex = productIndex;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                bool copper = product.Currency == "铜钱";

                _tint = new QualityTint(
                    InkWashTheme.QualityColor(product.Quality),
                    product.Icon,
                    IconAccent(product.Quality))
                {
                    Parent = this
                };

                _nameLabel = new Label
                {
                    Text = product.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                    TextColor = InkWashTheme.TextDefault,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _qualityTag = new QualityTag(product.Quality, product.QualityLabel)
                {
                    Parent = this
                };

                _priceGlyphLabel = new Label
                {
                    Text = copper ? "◎" : "◆",
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = copper ? InkWashTheme.PaperAged : InkWashTheme.GoldPrimary,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _priceValueLabel = new Label
                {
                    Text = FormatNumber(product.Price),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = copper ? InkWashTheme.TextDefault : InkWashTheme.GoldBright,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _priceCurrencyLabel = new Label
                {
                    Text = product.Currency,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextTertiary,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _buyButton = new InkButton
                {
                    Text = "购买",
                    Variant = InkButtonVariant.Vermilion,
                    ButtonSize = InkButtonSize.Sm,
                    Parent = this
                };
                _buyButton.Clicked += () => BuyClicked?.Invoke();
            }

            /// <summary>切换选中态（设计稿 .shop-card-selected）。</summary>
            public void SetSelected(bool selected)
            {
                _selected = selected;
                _nameLabel.TextColor = selected ? InkWashTheme.GoldBright : InkWashTheme.TextDefault;
            }

            /// <summary>布局卡片内部元素（由页面布局统一调用）。</summary>
            public void LayoutContent()
            {
                const float padding = 16f;
                float cardW = Width;

                _tint.Location = new Float2(padding, padding);
                _tint.Size = new Float2(Mathf.Max(0f, cardW - padding * 2f), 80f);

                float nameY = padding + 80f + 10f;
                const float tagWidth = 46f;
                _nameLabel.Location = new Float2(padding, nameY);
                _nameLabel.Size = new Float2(Mathf.Max(0f, cardW - padding * 2f - tagWidth - 8f), 20f);

                _qualityTag.Location = new Float2(cardW - padding - tagWidth, nameY);
                _qualityTag.Size = new Float2(tagWidth, 20f);

                float priceY = nameY + 20f + 10f;
                _priceGlyphLabel.Location = new Float2(padding, priceY);
                _priceGlyphLabel.Size = new Float2(16f, 28f);
                _priceValueLabel.Location = new Float2(padding + 18f, priceY);
                _priceValueLabel.Size = new Float2(46f, 28f);
                _priceCurrencyLabel.Location = new Float2(padding + 66f, priceY);
                _priceCurrencyLabel.Size = new Float2(40f, 28f);

                _buyButton.Location = new Float2(cardW - padding - 64f, priceY);
                _buyButton.Size = new Float2(64f, 28f);
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(bounds, InkWashTheme.RadiusLg, InkWashTheme.Panel);
                base.Draw();

                Color border = _selected
                    ? InkWashTheme.VermilionPrimary
                    : _hovered ? InkWashTheme.BorderGoldStrong : InkWashTheme.BorderGold;
                InkRenderHelper.DrawRoundedRectangle(bounds, InkWashTheme.RadiusLg, border, 1f);
            }
        }

        /// <summary>确认面板「礼包内容」行：名称 + ×数量。</summary>
        private class ContentViewRow : ContainerControl
        {
            private readonly Label _nameLabel;
            private readonly Label _countLabel;

            public ContentViewRow(ContentView item)
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _nameLabel = new Label
                {
                    Text = item.Name,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    TextColor = WithAlpha(InkWashTheme.TextOnPaper, 0.82f),
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                _countLabel = new Label
                {
                    Text = "×" + item.Count,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                    TextColor = InkWashTheme.TextOnPaper,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            /// <summary>布局行内两列（名称左 / 数量右）。</summary>
            public void LayoutContent()
            {
                _nameLabel.Location = Float2.Zero;
                _nameLabel.Size = new Float2(Mathf.Max(0f, Width - 64f), Height);
                _countLabel.Location = new Float2(Mathf.Max(0f, Width - 56f), 0f);
                _countLabel.Size = new Float2(56f, Height);
            }
        }

        /// <summary>
        /// 纸色面板上的数量增减按钮（设计稿 .qty-btn）：32x32 圆角 + 暗色描边 + 纸面文字。
        /// </summary>
        private class PaperQtyButton : Button
        {
            public PaperQtyButton(string text)
            {
                Text = text;
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 15f);
                BorderThickness = 1f;
                TextColor = InkWashTheme.TextOnPaper;
                ApplyNormal();
            }

            private void ApplyNormal()
            {
                BackgroundColor = WithAlpha(InkWashTheme.TextOnPaper, 0.06f);
                BorderColor = WithAlpha(InkWashTheme.TextOnPaper, 0.2f);
                TextColor = InkWashTheme.TextOnPaper;
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                BackgroundColor = WithAlpha(InkWashTheme.TextOnPaper, 0.12f);
                BorderColor = WithAlpha(InkWashTheme.TextOnPaper, 0.35f);
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                ApplyNormal();
            }
        }

        /// <summary>
        /// 纸色面板上的描边按钮（设计稿 .paper-btn-outline，用于「取消」）。
        /// </summary>
        private class PaperOutlineButton : Button
        {
            public PaperOutlineButton(string text)
            {
                Text = text;
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f);
                BorderThickness = 1f;
                ApplyNormal();
            }

            private void ApplyNormal()
            {
                BackgroundColor = Color.Transparent;
                BorderColor = WithAlpha(InkWashTheme.TextOnPaper, 0.28f);
                TextColor = InkWashTheme.TextOnPaper;
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                BackgroundColor = WithAlpha(InkWashTheme.TextOnPaper, 0.06f);
                BorderColor = WithAlpha(InkWashTheme.TextOnPaper, 0.45f);
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                ApplyNormal();
            }
        }

        // ───────────────────────── 构造 ─────────────────────────

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
                BuildAtmosphere();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildContentArea();
                BuildFooter();
                RefreshBalances();
                SelectTab(0);
                SelectProduct(0);
                ApplyLayout();

                // 顶栏身份接真实数据（铜钱在页内是示例文案，暂不覆盖）
                InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelTag);
                InkPlayerHeaderBinder.BindSect(_playerSectLabel);

                // 从侧栏直接进店时没有商人上下文：标题行标注这是示例货架
                ApplyDemoShelfHint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MenuShopPage] init: {ex.Message}");
            }
        }

        /// <summary>设计稿商品数据（8 个银两商品 + 3 个铜钱商品，共 9 件）。</summary>
        private void InitData()
        {
            _products = new ShopProduct[]
            {
                new ShopProduct
                {
                    Name = "新手大礼包", QualityLabel = "传世", Quality = InkWashTheme.InkQuality.Legendary,
                    Price = 328, Currency = "银两", Icon = "礼", Category = "礼包",
                    Contents = new[]
                    {
                        new ContentView { Name = "经验丹", Count = 10 },
                        new ContentView { Name = "修复锤", Count = 5 },
                        new ContentView { Name = "回城符", Count = 3 },
                        new ContentView { Name = "体力丹", Count = 5 },
                        new ContentView { Name = "银两", Count = 500 },
                    }
                },
                new ShopProduct
                {
                    Name = "神秘宝箱", QualityLabel = "史", Quality = InkWashTheme.InkQuality.Epic,
                    Price = 200, Currency = "银两", Icon = "匣", Category = "礼包",
                    Contents = new[]
                    {
                        new ContentView { Name = "随机装备", Count = 1 },
                        new ContentView { Name = "随机材料", Count = 3 },
                    }
                },
                new ShopProduct
                {
                    Name = "精良装备箱", QualityLabel = "珍", Quality = InkWashTheme.InkQuality.Rare,
                    Price = 80, Currency = "银两", Icon = "箱", Category = "装备",
                    Contents = new[] { new ContentView { Name = "精良装备", Count = 1 } }
                },
                new ShopProduct
                {
                    Name = "精良武器", QualityLabel = "良", Quality = InkWashTheme.InkQuality.Uncommon,
                    Price = 50, Currency = "银两", Icon = "剑", Category = "装备",
                    Contents = new[] { new ContentView { Name = "精良武器", Count = 1 } }
                },
                new ShopProduct
                {
                    Name = "经验丹×10", QualityLabel = "良", Quality = InkWashTheme.InkQuality.Uncommon,
                    Price = 20, Currency = "银两", Icon = "丹", Category = "消耗品",
                    Contents = new[] { new ContentView { Name = "经验丹", Count = 10 } }
                },
                new ShopProduct
                {
                    Name = "染色剂套装", QualityLabel = "珍", Quality = InkWashTheme.InkQuality.Rare,
                    Price = 120, Currency = "银两", Icon = "染", Category = "外观",
                    Contents = new[] { new ContentView { Name = "染色剂", Count = 10 } }
                },
                new ShopProduct
                {
                    Name = "修复锤×5", QualityLabel = "凡", Quality = InkWashTheme.InkQuality.Common,
                    Price = 500, Currency = "铜钱", Icon = "锤", Category = "消耗品",
                    Contents = new[] { new ContentView { Name = "修复锤", Count = 5 } }
                },
                new ShopProduct
                {
                    Name = "回城符×3", QualityLabel = "凡", Quality = InkWashTheme.InkQuality.Common,
                    Price = 300, Currency = "铜钱", Icon = "符", Category = "消耗品",
                    Contents = new[] { new ContentView { Name = "回城符", Count = 3 } }
                },
                new ShopProduct
                {
                    Name = "体力丹×5", QualityLabel = "凡", Quality = InkWashTheme.InkQuality.Common,
                    Price = 200, Currency = "铜钱", Icon = "体", Category = "消耗品",
                    Contents = new[] { new ContentView { Name = "体力丹", Count = 5 } }
                },
            };
        }

        // ───────────────────────── 构建 ─────────────────────────

        /// <summary>全局水墨背景层 + 暗角晕影（设计稿 ink-bg-layer / ink-vignette）。</summary>
        private void BuildAtmosphere()
        {
            // 背景层/暗角由外壳统一提供（本页为沉浸式全屏页），页面不再自绘。
        }

        /// <summary>左侧 240px 导航栏：品牌「混沌世界」+ 主导航 + 休闲模式快捷出口 + 次级导航 + 设置。</summary>
        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                BackgroundColor = InkWashTheme.Void,
                Parent = this
            };

            _brandBlock = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _sidebar
            };

            _brandLabel = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _brandBlock
            };

            _brandDivider = new InkDivider
            {
                Parent = _brandBlock
            };

            // 侧边栏导航：共用控件渲染 InkSidebarNav 的完整清单（分组 + 超出可滚动）
            _sidebarList = new InkSidebarList(InkPageDomIds.NavShop)
            {
                Parent = _sidebar,
            };
            _sidebarList.NavigationRequested += domId => NavigationRequested?.Invoke(domId);

            // 底部设置
            _sidebarFooter = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _sidebar
            };

            _sidebarFooterDivider = new InkDivider
            {
                Parent = _sidebarFooter
            };

            _settingsNav = new NavRow("设置")
            {
                Parent = _sidebarFooter
            };
            _settingsNav.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.NavSettings);
        }

        /// <summary>右侧主区域容器（顶部信息栏 + 内容区 + 页脚）。</summary>
        private void BuildMainArea()
        {
            _mainArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = this
            };
        }

        /// <summary>顶部 60px 角色信息栏：返回战场 / 头像 / 无名侠 / Lv.42 / 逍遥派 / 铜钱 / 银两。</summary>
        private void BuildTopHeader()
        {
            _topHeader = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _mainArea
            };

            _backButton = new InkButton
            {
                Text = "← 返回战场",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Parent = _topHeader
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            _playerAvatar = new CircleAvatar("侠")
            {
                Parent = _topHeader
            };

            _playerNameLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = InkWashTheme.TextDefault,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _playerLevelTag = new InkTag
            {
                Text = "—",
                TagVariant = InkTagVariant.Vermilion,
                Parent = _topHeader
            };

            _playerSectLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextSecondary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _copperGlyphLabel = new Label
            {
                Text = "◎",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _copperTitleLabel = new Label
            {
                Text = "铜钱",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _copperValueLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _silverGlyphLabel = new Label
            {
                Text = "◆",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.GoldPrimary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _silverTitleLabel = new Label
            {
                Text = "银两",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };

            _silverValueLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _topHeader
            };
        }

        /// <summary>标题行（竖排「市集」+ 商店 + 分类标签）+ 商品网格 + 右侧纸色确认面板。</summary>
        private void BuildContentArea()
        {
            _contentArea = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _mainArea
            };

            _verticalTitle = new InkVerticalTitle
            {
                Text = "市集",
                FontSize = 26f,
                Parent = _contentArea
            };

            _pageTitleLabel = new Label
            {
                Text = "商店",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.TextBrand,
                VerticalAlignment = TextAlignment.Center,
                Parent = _contentArea
            };

            _demoShelfLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _contentArea
            };

            _tabBar = new Panel
            {
                BackgroundColor = InkWashTheme.BaseTertiary,
                ClipChildren = false,
                Parent = _contentArea
            };

            _tabs = new List<TabButton>();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tabIndex = i;
                var tab = new TabButton(TabNames[i])
                {
                    Parent = _tabBar
                };
                tab.Clicked += () => SelectTab(tabIndex);
                _tabs.Add(tab);
            }

            // 商品网格
            _gridScroll = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical,
                Parent = _contentArea
            };

            _gridContent = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _gridScroll
            };

            _cards = new List<ProductCard>();
            for (int i = 0; i < _products.Length; i++)
            {
                int index = i;
                var card = new ProductCard(i, _products[i])
                {
                    Parent = _gridContent
                };
                card.Clicked += () => SelectProduct(index);
                card.BuyClicked += () => OnCardBuy(index);
                _cards.Add(card);
            }

            _gridEmptyHint = new Label
            {
                Text = "该分类暂无商品",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Visible = false,
                Parent = _gridScroll
            };

            BuildDetailPanel();
        }

        /// <summary>右侧 300px 纸色确认面板（礼包内容 / 单价 / 数量 / 总价 / 当前银两 / 操作按钮）。</summary>
        private void BuildDetailPanel()
        {
            _asidePanel = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _contentArea
            };

            _paperPanel = new InkPaperPanel
            {
                Parent = _asidePanel
            };

            _paperCorners = new InkCornerDeco
            {
                Parent = _paperPanel
            };

            _detailScroll = new Panel
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical,
                Parent = _paperPanel
            };

            _detailContent = new Panel
            {
                BackgroundColor = Color.Transparent,
                Parent = _detailScroll
            };

            _detailTitleLabel = new Label
            {
                Text = "新手大礼包",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 17f),
                TextColor = InkWashTheme.TextOnPaper,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _detailQualityTag = new QualityTag(InkWashTheme.InkQuality.Legendary, "传世")
            {
                Parent = _detailContent
            };

            _contentsTitleLabel = new Label
            {
                Text = "礼包内容",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _contentRows = new List<ContentViewRow>();

            _detailDivider = new InkDivider
            {
                Parent = _detailContent
            };

            _unitPriceTitleLabel = new Label
            {
                Text = "单价",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _unitPriceValueLabel = new Label
            {
                Text = "328",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _unitPriceCurrencyLabel = new Label
            {
                Text = "银两",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _qtyTitleLabel = new Label
            {
                Text = "数量",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _qtyMinusButton = new PaperQtyButton("−")
            {
                Parent = _detailContent
            };
            _qtyMinusButton.Clicked += () => OnQtyChanged(-1);

            _qtyValueLabel = new Label
            {
                Text = "1",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _qtyPlusButton = new PaperQtyButton("+")
            {
                Parent = _detailContent
            };
            _qtyPlusButton.Clicked += () => OnQtyChanged(1);

            _totalBox = new Panel
            {
                BackgroundColor = WithAlpha(InkWashTheme.VermilionPrimary, 0.08f),
                Parent = _detailContent
            };

            _totalTitleLabel = new Label
            {
                Text = "总价",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _totalBox
            };

            _totalValueLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 18f),
                TextColor = InkWashTheme.VermilionPrimary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _totalBox
            };

            _totalCurrencyLabel = new Label
            {
                Text = "银两",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _totalBox
            };

            _balanceTitleLabel = new Label
            {
                Text = "当前银两",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperDark,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _balanceValueLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _balanceHintLabel = new Label
            {
                Text = "（刚好够）",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.QualityUncommon,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };

            _confirmButton = new InkButton
            {
                Text = "确认购买",
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Md,
                Parent = _detailContent
            };
            _confirmButton.Clicked += OnConfirmPurchase;

            _cancelButton = new PaperOutlineButton("取消")
            {
                Parent = _detailContent
            };
            _cancelButton.Clicked += OnCancelPurchase;

            _actionHintLabel = new Label
            {
                Text = "选中商品后可调整数量再确认购买",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = WithAlpha(InkWashTheme.TextOnPaper, 0.7f),
                VerticalAlignment = TextAlignment.Center,
                Parent = _detailContent
            };
        }

        /// <summary>页脚：混沌世界 · v1.2.0 ／ 商店系统 · 仅供参考。</summary>
        private void BuildFooter()
        {
            _footer = new Panel
            {
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _mainArea
            };

            _footerDivider = new InkDivider
            {
                Parent = _footer
            };

            _footerLeftLabel = new Label
            {
                Text = "混沌世界 · v1.2.0",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };

            _footerRightLabel = new Label
            {
                Text = "商店系统 · 仅供参考",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Parent = _footer
            };
        }

        // ───────────────────────── 交互 ─────────────────────────

        /// <summary>切换分类标签（推荐展示全部，其余按分类过滤）。</summary>
        private void SelectTab(int index)
        {
            _selectedTab = Mathf.Clamp(index, 0, TabNames.Length - 1);

            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].Active = i == _selectedTab;

            for (int i = 0; i < _cards.Count; i++)
                _cards[i].Visible = IsVisibleInTab(_products[i]);

            int visibleCount = CountVisibleCards();
            _gridEmptyHint.Visible = visibleCount == 0;

            if (visibleCount == 0)
            {
                // 该分类无商品：隐藏确认面板并展示空态提示
                _asidePanel.Visible = false;
                _paperPanel.Visible = false;
                ApplyLayout();
                return;
            }

            if (!IsVisibleInTab(_products[_selectedIndex]))
            {
                SelectProduct(FirstVisibleProductIndex());
                return;
            }

            _asidePanel.Visible = true;
            _paperPanel.Visible = true;
            RefreshDetail();
        }

        /// <summary>选中商品：刷新卡片选中态与右侧确认面板。</summary>
        private void SelectProduct(int index)
        {
            if (index < 0 || index >= _products.Length)
                return;

            _selectedIndex = index;
            _qty = 1;

            for (int i = 0; i < _cards.Count; i++)
                _cards[i].SetSelected(i == _selectedIndex);

            RefreshDetail();
        }

        /// <summary>卡片内「购买」按钮：选中该商品并提示前往确认面板。</summary>
        private void OnCardBuy(int index)
        {
            SelectProduct(index);
            _actionHintLabel.Text = $"已选中「{_products[index].Name}」，请在右侧确认购买";
        }

        /// <summary>数量增减（1 ~ 99）。</summary>
        private void OnQtyChanged(int delta)
        {
            _qty = Mathf.Clamp(_qty + delta, 1, MaxQty);
            RefreshDetailTotals();
        }

        /// <summary>确认购买：校验余额并按数量扣减，刷新顶部货币与面板数值。</summary>
        private void OnConfirmPurchase()
        {
            var product = CurrentProduct();
            if (product == null)
                return;

            // 服务端模式：价格、库存、余额全由服务端裁决，本地只发请求并显示回执。
            if (_vendorEntityId != 0 && _selectedIndex >= 0 && _selectedIndex < _serverSlotByCard.Count)
            {
                if (HundunWorld.Game.Network.ShopClientService.Instance.Buy(_vendorEntityId, _serverSlotByCard[_selectedIndex], _qty))
                {
                    _actionHintLabel.Text = "已发送购买请求…";
                    _actionHintLabel.TextColor = InkWashTheme.JadeDeep;
                }
                else
                {
                    _actionHintLabel.Text = "未连接服务器，无法购买";
                    _actionHintLabel.TextColor = InkWashTheme.VermilionPrimary;
                }

                return;
            }

            // 没有商人上下文时货架是目录样例：既不扣本地余额也不报"已购入"，
            // 否则等于用编出来的余额演一次购买成功。
            if (_actionHintLabel != null)
            {
                _actionHintLabel.Text = "未与商人交谈，示例货架不可购买";
                _actionHintLabel.TextColor = InkWashTheme.VermilionPrimary;
            }
        }

        /// <summary>取消本次购买：数量复位为 1。</summary>
        private void OnCancelPurchase()
        {
            _qty = 1;
            RefreshDetailTotals();
            _actionHintLabel.Text = "已取消本次购买";
            _actionHintLabel.TextColor = WithAlpha(InkWashTheme.TextOnPaper, 0.7f);
        }

        // ───────────────────────── 数据刷新 ─────────────────────────

        /// <summary>当前选中商品。</summary>
        private ShopProduct CurrentProduct()
        {
            if (_products == null || _selectedIndex < 0 || _selectedIndex >= _products.Length)
                return null;
            return _products[_selectedIndex];
        }

        /// <summary>商品是否属于当前分类（推荐页展示全部）。</summary>
        // ───────────────────────── 服务端货架 ─────────────────────────

        /// <summary>
        /// 打开某个 NPC 的货架（由世界交互入口调用）。
        /// <para>连接不上时页面保持演示数据，但会明确写一行"未连接"，不假装成功。</para>
        /// </summary>
        public void OpenVendor(ulong entityId)
        {
            _vendorEntityId = entityId;
            _lastShopRevision = -1;

            if (!HundunWorld.Game.Network.ShopClientService.Instance.Open(entityId) && _actionHintLabel != null)
            {
                _actionHintLabel.Text = "未连接服务器：商人窗口暂时只能看演示数据";
                _actionHintLabel.TextColor = InkWashTheme.VermilionPrimary;
            }
            ApplyDemoShelfHint();
        }

        /// <summary>没有商人上下文时在标题行标注"示例货架"，绑上商人后清空。</summary>
        private void ApplyDemoShelfHint()
        {
            if (_demoShelfLabel == null)
                return;

            _demoShelfLabel.Text = _vendorEntityId == 0 ? "未与商人交谈 · 以下为货架示例" : string.Empty;
        }

        /// <summary>
        /// 回包在连接线程写缓存，这里在主线程发现版本变了才重绘（与任务页同一条纪律）。
        /// </summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 交互入口可能已经把页面打开（路由会复用实例），这里取走"这次要绑定哪个商人"。
            var requestedVendor = HundunWorld.Game.Network.ShopClientService.Instance.TakeRequestedVendor();
            if (requestedVendor != 0) OpenVendor(requestedVendor);

            if (_vendorEntityId == 0) return;

            var service = HundunWorld.Game.Network.ShopClientService.Instance;
            var revision = service.Revision;
            if (revision == _lastShopRevision) return;

            _lastShopRevision = revision;
            ApplyServerShelf(service.Items, service.Gold, service.LastNotice, service.LastReason);
        }

        /// <summary>
        /// 把服务端货架映射成卡片并重建。
        /// <para><b>必须重建而不是改数据</b>：<see cref="ProductCard"/> 在构造时就把名称/价格读进子控件，
        /// 只改 <see cref="_products"/> 里的对象不会刷新任何一个字。</para>
        /// </summary>
        private void ApplyServerShelf(ShopItemDto[] items, long gold, string notice, string reason)
        {
            _serverSlotByCard.Clear();

            var list = new System.Collections.Generic.List<ShopProduct>(items?.Length ?? 0);
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null) continue;

                    var def = HundunWorld.Game.Inventory.ItemCatalog.Resolve(item.ItemId);
                    var quality = def != null && def.Quality >= 0 && def.Quality <= 4
                        ? (InkWashTheme.InkQuality)def.Quality
                        : InkWashTheme.InkQuality.Common;

                    list.Add(new ShopProduct
                    {
                        Name = def != null ? def.Name : ("物品 #" + item.ItemId),
                        QualityLabel = def != null ? def.TypeName : string.Empty,
                        Quality = quality,
                        Price = (int)Math.Min(int.MaxValue, Math.Max(0L, item.BuyPrice)),
                        Currency = "铜钱",
                        Icon = def != null ? def.Glyph : "货",
                        // 商人货架不分类：放"推荐"（0 号页本来就展示全部），避免被误塞进某个分类后看不见。
                        Category = TabNames[0],
                        Contents = System.Array.Empty<ContentView>(),
                    });
                    _serverSlotByCard.Add(item.Slot);
                }
            }

            _products = list.ToArray();
            RebuildCards();

            _copperBalance = (int)Math.Min(int.MaxValue, Math.Max(0L, gold));
            _hasCopperSnapshot = true;
            RefreshBalances();

            if (!string.IsNullOrEmpty(notice) && _actionHintLabel != null)
            {
                _actionHintLabel.Text = notice;
                _actionHintLabel.TextColor = string.IsNullOrEmpty(reason)
                    ? InkWashTheme.JadeDeep
                    : InkWashTheme.VermilionPrimary;
            }

            // SelectTab 会重算每张卡片的可见性（含空货架提示），ApplyLayout 再把它们排开。
            SelectTab(_selectedTab);
            ApplyLayout();
        }

        /// <summary>按当前 <see cref="_products"/> 重建卡片；旧的必须解绑并释放，否则会叠在网格里。</summary>
        private void RebuildCards()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == null) continue;
                _cards[i].Parent = null;
                _cards[i].Dispose();
            }

            _cards.Clear();

            for (int i = 0; i < _products.Length; i++)
            {
                int index = i;
                var card = new ProductCard(i, _products[i]) { Parent = _gridContent };
                card.Clicked += () => SelectProduct(index);
                card.BuyClicked += () => OnCardBuy(index);
                _cards.Add(card);
            }

            _selectedIndex = _products.Length > 0 ? 0 : -1;
        }

        private bool IsVisibleInTab(ShopProduct product)
        {
            if (product == null)
                return false;
            return _selectedTab == 0 || product.Category == TabNames[_selectedTab];
        }

        private int CountVisibleCards()
        {
            int count = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Visible)
                    count++;
            }
            return count;
        }

        private int FirstVisibleProductIndex()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Visible)
                    return i;
            }
            return 0;
        }

        /// <summary>刷新顶部信息栏货币数值。铜钱没有服务端快照、银两根本没有下行通道时显示占位符。</summary>
        private void RefreshBalances()
        {
            if (_copperValueLabel != null)
                _copperValueLabel.Text = _hasCopperSnapshot ? FormatNumber(_copperBalance) : "—";
            if (_silverValueLabel != null)
                _silverValueLabel.Text = "—";
        }

        /// <summary>按当前选中商品重建确认面板内容（礼包内容行 + 数值）。</summary>
        private void RefreshDetail()
        {
            var product = CurrentProduct();
            if (product == null)
            {
                _asidePanel.Visible = false;
                return;
            }

            _asidePanel.Visible = true;
            _paperPanel.Visible = true;

            foreach (var row in _contentRows)
                row.Dispose();
            _contentRows.Clear();

            foreach (var item in product.Contents)
            {
                _contentRows.Add(new ContentViewRow(item)
                {
                    Parent = _detailContent
                });
            }

            _detailTitleLabel.Text = product.Name;
            _detailQualityTag.Text = product.QualityLabel;
            _detailQualityTag.ApplyQuality(product.Quality);

            RefreshDetailTotals();
            ApplyLayout();
        }

        /// <summary>刷新确认面板的单价 / 数量 / 总价 / 当前余额与提示。</summary>
        private void RefreshDetailTotals()
        {
            var product = CurrentProduct();
            if (product == null)
                return;

            int total = product.Price * _qty;
            int balance = product.Currency == "银两" ? _silverBalance : _copperBalance;

            _unitPriceValueLabel.Text = FormatNumber(product.Price);
            _unitPriceCurrencyLabel.Text = product.Currency;

            _qtyValueLabel.Text = _qty.ToString();

            _totalValueLabel.Text = FormatNumber(total);
            _totalCurrencyLabel.Text = product.Currency;

            _balanceValueLabel.Text = FormatNumber(balance);
            _balanceTitleLabel.Text = product.Currency == "银两" ? "当前银两" : "当前铜钱";

            if (balance == total)
            {
                _balanceHintLabel.Text = "（刚好够）";
                _balanceHintLabel.TextColor = InkWashTheme.QualityUncommon;
            }
            else if (balance > total)
            {
                _balanceHintLabel.Text = "（余额充足）";
                _balanceHintLabel.TextColor = InkWashTheme.JadeDeep;
            }
            else
            {
                _balanceHintLabel.Text = "（余额不足）";
                _balanceHintLabel.TextColor = InkWashTheme.VermilionPrimary;
            }
        }

        // ───────────────────────── 布局 ─────────────────────────

        private void ApplyLayout()
        {
            float sw = Width > 0f ? Width : _screenSize.X;
            float sh = Height > 0f ? Height : _screenSize.Y;
            if (sw <= 0f || sh <= 0f)
                return;

            // ── 左侧导航栏 ──
            _sidebar.Location = Float2.Zero;
            _sidebar.Size = new Float2(SidebarWidth, sh);

            _brandBlock.Location = Float2.Zero;
            _brandBlock.Size = new Float2(SidebarWidth, BrandHeight);
            _brandLabel.Location = Float2.Zero;
            _brandLabel.Size = new Float2(SidebarWidth, BrandHeight - 1f);
            _brandDivider.Location = new Float2(0f, BrandHeight - 1f);
            _brandDivider.Size = new Float2(SidebarWidth, 1f);

            float navPanelH = Mathf.Max(0f, sh - BrandHeight - SidebarFooterHeight);
            _sidebarList.Layout(0f, BrandHeight, SidebarWidth, navPanelH);

            _sidebarFooter.Location = new Float2(0f, sh - SidebarFooterHeight);
            _sidebarFooter.Size = new Float2(SidebarWidth, SidebarFooterHeight);
            _sidebarFooterDivider.Location = Float2.Zero;
            _sidebarFooterDivider.Size = new Float2(SidebarWidth, 1f);
            _settingsNav.Location = new Float2(0f, 9f);
            _settingsNav.Size = new Float2(SidebarWidth, NavItemHeight);

            // ── 右侧主区域 ──
            float mainW = Mathf.Max(0f, sw - SidebarWidth);
            _mainArea.Location = new Float2(SidebarWidth, 0f);
            _mainArea.Size = new Float2(mainW, sh);

            LayoutTopHeader(mainW);

            _contentArea.Location = new Float2(0f, TopHeaderHeight);
            _contentArea.Size = new Float2(mainW, Mathf.Max(0f, sh - TopHeaderHeight - FooterHeight));

            _footer.Location = new Float2(0f, sh - FooterHeight);
            _footer.Size = new Float2(mainW, FooterHeight);
            _footerDivider.Location = Float2.Zero;
            _footerDivider.Size = new Float2(mainW, 1f);
            _footerLeftLabel.Location = new Float2(ContentPadding, 12f);
            _footerLeftLabel.Size = new Float2(220f, 20f);
            _footerRightLabel.Location = new Float2(Mathf.Max(0f, mainW - ContentPadding - 220f), 12f);
            _footerRightLabel.Size = new Float2(220f, 20f);

            LayoutContentArea();
        }

        /// <summary>顶部信息栏内部布局（左：返回战场 + 角色信息；右：铜钱 / 银两）。</summary>
        private void LayoutTopHeader(float mainW)
        {
            _topHeader.Location = Float2.Zero;
            _topHeader.Size = new Float2(mainW, TopHeaderHeight);

            float x = ContentPadding;
            _backButton.Location = new Float2(x, (TopHeaderHeight - 28f) * 0.5f);
            _backButton.Size = new Float2(104f, 28f);
            x += 104f + 12f;

            _playerAvatar.Location = new Float2(x, (TopHeaderHeight - 40f) * 0.5f);
            _playerAvatar.Size = new Float2(40f, 40f);
            x += 40f + 12f;

            _playerNameLabel.Location = new Float2(x, 18f);
            _playerNameLabel.Size = new Float2(80f, 24f);
            x += 84f;

            _playerLevelTag.Location = new Float2(x, 20f);
            _playerLevelTag.Size = new Float2(52f, 20f);
            x += 52f + 10f;

            _playerSectLabel.Location = new Float2(x, 20f);
            _playerSectLabel.Size = new Float2(80f, 20f);

            // 右侧货币：银两 → 铜钱（由右向左排布）
            const float valueW = 64f;
            const float titleW = 38f;
            const float glyphW = 18f;
            const float innerGap = 6f;
            const float groupGap = 24f;

            float rightEdge = Mathf.Max(0f, mainW - ContentPadding);
            float silverGroupW = glyphW + innerGap + titleW + innerGap + valueW;
            float silverX = Mathf.Max(0f, rightEdge - silverGroupW);

            _silverGlyphLabel.Location = new Float2(silverX, 20f);
            _silverGlyphLabel.Size = new Float2(glyphW, 20f);
            _silverTitleLabel.Location = new Float2(silverX + glyphW + innerGap, 20f);
            _silverTitleLabel.Size = new Float2(titleW, 20f);
            _silverValueLabel.Location = new Float2(silverX + glyphW + innerGap + titleW + innerGap, 20f);
            _silverValueLabel.Size = new Float2(valueW, 20f);

            float copperGroupW = glyphW + innerGap + titleW + innerGap + valueW;
            float copperX = Mathf.Max(0f, silverX - groupGap - copperGroupW);

            _copperGlyphLabel.Location = new Float2(copperX, 20f);
            _copperGlyphLabel.Size = new Float2(glyphW, 20f);
            _copperTitleLabel.Location = new Float2(copperX + glyphW + innerGap, 20f);
            _copperTitleLabel.Size = new Float2(titleW, 20f);
            _copperValueLabel.Location = new Float2(copperX + glyphW + innerGap + titleW + innerGap, 20f);
            _copperValueLabel.Size = new Float2(valueW, 20f);
        }

        /// <summary>内容区布局：标题行 + 商品网格 + 右侧纸色确认面板。</summary>
        private void LayoutContentArea()
        {
            float contentW = _contentArea.Width;
            float contentH = _contentArea.Height;
            if (contentW <= 0f || contentH <= 0f)
                return;

            // 标题行：竖排「市集」+ 商店 + 分类标签（右对齐）
            _verticalTitle.Location = new Float2(ContentPadding, ContentPadding - 4f);
            _verticalTitle.Size = new Float2(34f, TitleRowHeight);

            _pageTitleLabel.Location = new Float2(ContentPadding + 46f, ContentPadding + 8f);
            _pageTitleLabel.Size = new Float2(180f, 44f);

            float tabBarW = TabNames.Length * (TabWidth + 4f) + 8f;
            _tabBar.Location = new Float2(
                Mathf.Max(0f, contentW - ContentPadding - tabBarW),
                ContentPadding + (TitleRowHeight - TabBarHeight) * 0.5f);
            _tabBar.Size = new Float2(tabBarW, TabBarHeight);

            if (_demoShelfLabel != null)
            {
                float hintX = ContentPadding + 240f;
                _demoShelfLabel.Location = new Float2(hintX, ContentPadding + 18f);
                _demoShelfLabel.Size = new Float2(
                    Mathf.Max(0f, _tabBar.Location.X - hintX - 12f), 20f);
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Location = new Float2(4f + i * (TabWidth + 4f), 3f);
                _tabs[i].Size = new Float2(TabWidth, TabBarHeight - 6f);
            }

            // 商品网格
            float gridTop = ContentPadding + TitleRowHeight + 20f;
            float availH = Mathf.Max(0f, contentH - gridTop - ContentPadding);
            float asideW = Mathf.Min(AsideWidth, Mathf.Max(0f, contentW * 0.32f));
            float gridW = Mathf.Max(0f, contentW - ContentPadding * 2f - asideW - GridAsideGap);

            _gridScroll.Location = new Float2(ContentPadding, gridTop);
            _gridScroll.Size = new Float2(gridW, availH);

            LayoutGrid(gridW, availH);

            _asidePanel.Location = new Float2(ContentPadding + gridW + GridAsideGap, gridTop);
            _asidePanel.Size = new Float2(asideW, availH);

            LayoutDetailPanel(asideW, availH);
        }

        /// <summary>商品网格布局：按可用宽度自适应 1~3 列，仅排布当前分类可见的卡片。</summary>
        private void LayoutGrid(float gridW, float availH)
        {
            int columns = gridW >= 640f ? 3 : gridW >= 420f ? 2 : 1;
            float cardW = Mathf.Max(120f, (gridW - CardGap * (columns - 1)) / columns);

            int slot = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (!card.Visible)
                    continue;

                int row = slot / columns;
                int col = slot % columns;
                card.Location = new Float2(col * (cardW + CardGap), row * (CardHeight + CardGap));
                card.Size = new Float2(cardW, CardHeight);
                card.LayoutContent();
                slot++;
            }

            int rows = slot > 0 ? (slot + columns - 1) / columns : 0;
            float contentH = rows > 0 ? rows * CardHeight + (rows - 1) * CardGap : availH;
            _gridContent.Location = Float2.Zero;
            _gridContent.Size = new Float2(Mathf.Max(0f, gridW - 16f), Mathf.Max(availH, contentH));

            _gridEmptyHint.Location = new Float2(0f, 24f);
            _gridEmptyHint.Size = new Float2(gridW, 24f);
        }

        /// <summary>纸色确认面板布局：内容自顶向下排布，面板高度按内容裁剪到可用高度。</summary>
        private void LayoutDetailPanel(float asideW, float availH)
        {
            if (_paperPanel == null)
                return;

            _paperPanel.Location = Float2.Zero;
            _paperPanel.Size = new Float2(asideW, availH);

            _paperCorners.Location = new Float2(4f, 4f);
            _paperCorners.Size = new Float2(Mathf.Max(0f, asideW - 8f), Mathf.Max(0f, availH - 8f));

            _detailScroll.Location = new Float2(12f, 12f);
            _detailScroll.Size = new Float2(Mathf.Max(0f, asideW - 24f), Mathf.Max(0f, availH - 24f));

            float innerW = Mathf.Max(0f, _detailScroll.Width - 10f);
            _detailContent.Location = Float2.Zero;
            _detailContent.Size = new Float2(innerW, availH);

            const float innerX = 4f;
            const float rowH = 22f;
            float dy = 2f;

            // 标题 + 品质标签
            const float tagW = 52f;
            _detailTitleLabel.Location = new Float2(innerX, dy);
            _detailTitleLabel.Size = new Float2(Mathf.Max(0f, innerW - tagW - 12f), 26f);

            _detailQualityTag.Location = new Float2(innerX + Mathf.Max(0f, innerW - tagW), dy + 2f);
            _detailQualityTag.Size = new Float2(tagW, rowH);
            dy += 30f + 14f;

            // 礼包内容
            _contentsTitleLabel.Location = new Float2(innerX, dy);
            _contentsTitleLabel.Size = new Float2(innerW, 18f);
            dy += 18f + 8f;

            for (int i = 0; i < _contentRows.Count; i++)
            {
                var row = _contentRows[i];
                row.Location = new Float2(innerX, dy);
                row.Size = new Float2(innerW, rowH);
                row.LayoutContent();
                dy += rowH;
                if (i < _contentRows.Count - 1)
                    dy += 8f;
            }

            dy += 12f;
            _detailDivider.Location = new Float2(innerX, dy);
            _detailDivider.Size = new Float2(innerW, 1f);
            dy += 1f + 12f;

            // 单价
            _unitPriceTitleLabel.Location = new Float2(innerX, dy);
            _unitPriceTitleLabel.Size = new Float2(60f, rowH);

            _unitPriceCurrencyLabel.Location = new Float2(innerX + Mathf.Max(0f, innerW - 40f), dy);
            _unitPriceCurrencyLabel.Size = new Float2(40f, rowH);

            _unitPriceValueLabel.Location = new Float2(innerX + Mathf.Max(0f, innerW - 40f - 56f), dy);
            _unitPriceValueLabel.Size = new Float2(56f, rowH);
            dy += rowH + 8f;

            // 数量
            const float qtyBtn = 32f;
            _qtyTitleLabel.Location = new Float2(innerX, dy);
            _qtyTitleLabel.Size = new Float2(60f, qtyBtn + 2f);

            float qtyRight = innerX + Mathf.Max(0f, innerW - qtyBtn);
            _qtyPlusButton.Location = new Float2(qtyRight, dy + 1f);
            _qtyPlusButton.Size = new Float2(qtyBtn, qtyBtn);

            _qtyValueLabel.Location = new Float2(Mathf.Max(0f, qtyRight - 40f), dy + 1f);
            _qtyValueLabel.Size = new Float2(34f, qtyBtn);

            _qtyMinusButton.Location = new Float2(Mathf.Max(0f, qtyRight - 40f - 8f - qtyBtn), dy + 1f);
            _qtyMinusButton.Size = new Float2(qtyBtn, qtyBtn);
            dy += qtyBtn + 2f + 12f;

            // 总价
            const float totalH = 56f;
            _totalBox.Location = new Float2(innerX, dy);
            _totalBox.Size = new Float2(innerW, totalH);

            _totalTitleLabel.Location = new Float2(12f, (totalH - rowH) * 0.5f);
            _totalTitleLabel.Size = new Float2(60f, rowH);

            _totalCurrencyLabel.Location = new Float2(Mathf.Max(0f, innerW - 40f - 12f), (totalH - rowH) * 0.5f);
            _totalCurrencyLabel.Size = new Float2(40f, rowH);

            _totalValueLabel.Location = new Float2(Mathf.Max(0f, innerW - 40f - 12f - 64f), (totalH - 24f) * 0.5f);
            _totalValueLabel.Size = new Float2(64f, 24f);
            dy += totalH + 12f;

            // 当前余额
            _balanceTitleLabel.Location = new Float2(innerX, dy);
            _balanceTitleLabel.Size = new Float2(80f, 20f);

            _balanceHintLabel.Location = new Float2(innerX + Mathf.Max(0f, innerW - 96f), dy);
            _balanceHintLabel.Size = new Float2(96f, 20f);

            _balanceValueLabel.Location = new Float2(innerX + Mathf.Max(0f, innerW - 96f - 60f), dy);
            _balanceValueLabel.Size = new Float2(60f, 20f);
            dy += 20f + 16f;

            // 操作按钮
            const float btnH = 36f;
            const float cancelW = 72f;
            _confirmButton.Location = new Float2(innerX, dy);
            _confirmButton.Size = new Float2(Mathf.Max(80f, innerW - cancelW - 8f), btnH);

            _cancelButton.Location = new Float2(innerX + Mathf.Max(0f, innerW - cancelW), dy);
            _cancelButton.Size = new Float2(cancelW, btnH);
            dy += btnH + 12f;

            // 操作反馈
            _actionHintLabel.Location = new Float2(innerX, dy);
            _actionHintLabel.Size = new Float2(innerW, 20f);
            dy += 20f + 8f;

            _detailContent.Size = new Float2(innerW, Mathf.Max(availH, dy));

            float panelH = Mathf.Min(Mathf.Max(240f, dy + 24f), availH);
            _paperPanel.Size = new Float2(asideW, panelH);
            _paperCorners.Size = new Float2(Mathf.Max(0f, asideW - 8f), Mathf.Max(0f, panelH - 8f));
            _detailScroll.Size = new Float2(Mathf.Max(0f, asideW - 24f), Mathf.Max(0f, panelH - 24f));
        }

        // ───────────────────────── 工具 ─────────────────────────

        /// <summary>品质强调色（设计稿 .q-tag-*：传世用朱红亮色，凡品用纸暗色）。</summary>
        private static Color QualityAccent(InkWashTheme.InkQuality quality)
        {
            switch (quality)
            {
                case InkWashTheme.InkQuality.Legendary: return InkWashTheme.VermilionBright;
                case InkWashTheme.InkQuality.Epic: return InkWashTheme.QualityEpic;
                case InkWashTheme.InkQuality.Rare: return InkWashTheme.QualityRare;
                case InkWashTheme.InkQuality.Uncommon: return InkWashTheme.QualityUncommon;
                default: return InkWashTheme.QualityCommon;
            }
        }

        /// <summary>品质色块图标色（设计稿：传世朱红亮、凡品纸暗、其余品质色）。</summary>
        private static Color IconAccent(InkWashTheme.InkQuality quality)
        {
            switch (quality)
            {
                case InkWashTheme.InkQuality.Legendary: return InkWashTheme.VermilionBright;
                case InkWashTheme.InkQuality.Common: return InkWashTheme.PaperDark;
                default: return InkWashTheme.QualityColor(quality);
            }
        }

        /// <summary>千分位数字（设计稿 12,450 形式）。</summary>
        private static string FormatNumber(int value)
        {
            return value.ToString("N0");
        }

        /// <summary>基于设计 Token 的颜色派生（仅调整透明度，不引入硬编码色值）。</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }
    }
}
